using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第六章《放过自己》——酒桌小演出（纯代码绘制）
///
/// 【摆在哪】
/// 场景里是一个 Node2D，原点在"你的"玻璃杯杯底中心（挂在烧烤摊左下角的台面上）。
/// 左边 (-190, 0) 立着啤酒瓶；右后方 (+240, -80) 是金艮的小杯子（0.8 倍大）。
///
/// 【倒酒】
/// PourNext()：瓶子朝杯子倾 0.4s → 酒线落下、杯里的酒涨 1/4（0.8s）→ 瓶子回正 0.4s。
/// 两个杯子一起涨（他那边的小杯子稍微晚一点点）。
/// 音效：酒线落下时 sfx_pour，半途再补一声更轻的（像酒面在起泡）。
///
/// 【碰杯】
/// Cheers()：两只杯子相向靠近 → "叮"（sfx_clink）+ 星光炸开 → 轻轻弹开又贴回去，
/// 然后就这么碰着停住，等外面对话收尾。
///
/// 【炭火】
/// 同一个文件底下的 EmberDust：烤炉上方慢慢飘的火星子（不多了，就几颗）。
/// </summary>
public partial class DrinkPour : Node2D
{
    // ==================== 杯子几何（杯底中心为原点） ====================
    private const float GlassBaseW = 44f;      // 杯内底部半宽
    private const float GlassTopW = 53f;       // 杯口内壁半宽
    private const float GlassBottomY = -8f;    // 杯内底部
    private const float GlassRimY = -150f;     // 杯口（内壁）
    private const float GlassOuterW = 58f;     // 杯壁外沿半宽

    // ==================== 酒瓶几何 ====================
    private const float BottleDx = -190f;      // 酒瓶立在这个 x（杯底中心为 0）
    private const float BottleH = 400f;        // 瓶高（瓶口到瓶底）
    private const float PourAngle = 0.56f;     // 倒酒时倾斜的角度（弧度，约 32°）

    // ==================== 金艮的小杯子 ====================
    private static readonly Vector2 FriendHome = new(240f, -80f); // 原位
    private static readonly Vector2 FriendClink = new(199f, -40f); // 碰杯点
    private static readonly Vector2 MineClink = new(95f, 2f);      // 我这边挪过去的位置
    private const float FriendScale = 0.8f;

    // ==================== 倒酒节奏 ====================
    private const float TiltIn = 0.4f;
    private const float HoldDur = 0.8f;
    private const float TiltOut = 0.4f;
    private const float PourTotal = TiltIn + HoldDur + TiltOut;

    // ==================== 状态 ====================
    private float fill;            // 主杯目标酒面（0~1）
    private float fillShown;       // 主杯当前画出来的酒面
    private float friendFill;
    private float friendFillShown;
    private float pourFromFill, pourFromFriend;

    private float pourT = -1f;     // 倒酒进度（<0 = 没在倒）
    private Action pourDone;
    private bool splashFired;      // 半途那声"起泡"放了没

    private float settleT;         // 倒完之后酒面晃一晃的余韵
    private const float SettleDur = 0.6f;

    private Vector2 myOffset;      // 碰杯时主杯整体挪动的偏移
    private Vector2 friendPos;     // 金艮杯子的当前位置（局部坐标）

    private float cheersT = -1f;   // 碰杯动画进度（<0 = 没在碰）
    private Action cheersDone;
    private bool clinkFired;
    private bool cheersStarted;

    private float time;            // 内部时间（酒里的小气泡用）

    public override void _Ready()
    {
        friendPos = FriendHome;
    }

    // ==================== 对外 API（章节脚本调用） ====================

    /// <summary>倒下一次：涨 1/4 杯。动画播完回调（回调时机 = 可以接着播对话了）</summary>
    public void PourNext(Action onDone)
    {
        if (pourT >= 0f)
            return; // 正在倒，忽略

        pourFromFill = fillShown;
        pourFromFriend = friendFillShown;
        fill = Mathf.Min(1f, fill + 0.25f);
        friendFill = Mathf.Min(1f, friendFill + 0.25f);

        pourT = 0f;
        pourDone = onDone;
        splashFired = false;
    }

    /// <summary>碰杯：两只杯子靠到一起，"叮"的一声（只生效一次）</summary>
    public void Cheers(Action onDone)
    {
        if (cheersStarted)
            return;
        cheersStarted = true;
        cheersT = 0f;
        cheersDone = onDone;
        clinkFired = false;
    }

    // ==================== 主循环 ====================

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;

        // ---- 倒酒 ----
        if (pourT >= 0f)
        {
            pourT += dt;

            float holdStart = TiltIn;

            // 酒面：主要在"酒线落着"的那段涨
            float k = Mathf.Clamp((pourT - holdStart) / HoldDur, 0f, 1f);
            fillShown = Mathf.Lerp(pourFromFill, fill, k);

            // 金艮的杯子：晚 0.15s 开始涨，节奏稍缓
            float kf = Mathf.Clamp((pourT - holdStart - 0.15f) / (HoldDur * 0.85f), 0f, 1f);
            friendFillShown = Mathf.Lerp(pourFromFriend, friendFill, kf);

            // 音效：酒线落下的瞬间 + 半途一声更轻的
            // （第三个参数是"抖动范围"不是绝对音高——绝对音高走第四个参数。
            //   2026-10-09 run3 踩坑：把 1.12 当抖动传，RandRange(-0.12,2.12)
            //   掷到 ≤0 时引擎报 "p_pitch_scale <= 0.0"）
            if (pourT >= holdStart && pourT - dt < holdStart)
                AudioManager.Instance?.PlaySfx(AudioManager.SfxPour, -10f, 0.04f, 1.12f);
            if (!splashFired && pourT >= holdStart + 0.45f)
            {
                splashFired = true;
                AudioManager.Instance?.PlaySfx(AudioManager.SfxPour, -17f, 0.05f, 1.3f);
            }

            if (pourT >= PourTotal)
            {
                pourT = -1f;
                fillShown = fill;
                friendFillShown = friendFill;
                settleT = SettleDur;
                var cb = pourDone;
                pourDone = null;
                cb?.Invoke();
            }
        }
        else if (settleT > 0f)
        {
            settleT = Mathf.Max(0f, settleT - dt);
        }

        // ---- 碰杯 ----
        if (cheersT >= 0f)
        {
            cheersT += dt;

            const float inDur = 0.7f;    // 靠近
            const float bounceDur = 0.26f; // 弹开又贴回去

            if (cheersT < inDur)
            {
                float p = EaseOutCubic(cheersT / inDur);
                myOffset = MineClink * p;
                friendPos = FriendHome.Lerp(FriendClink, p);
            }
            else if (cheersT < inDur + bounceDur)
            {
                float p = (cheersT - inDur) / bounceDur;

                if (!clinkFired)
                {
                    clinkFired = true;
                    FireClink();
                }

                // 轻轻分开一点，又贴回去
                float bump = Mathf.Sin(p * Mathf.Pi) * 7f;
                myOffset = MineClink - new Vector2(bump, 0f);
                friendPos = FriendClink + new Vector2(bump, 0f);
            }
            else
            {
                myOffset = MineClink;
                friendPos = FriendClink;
                cheersT = -1f;
                var cb = cheersDone;
                cheersDone = null;
                cb?.Invoke();
            }
        }

        // 酒里的小气泡一直在轻轻动，简单起见每帧重画（就这一个节点，很便宜）
        QueueRedraw();
    }

    private static float EaseOutCubic(float p) => 1f - Mathf.Pow(1f - p, 3f);

    /// <summary>叮——碰杯音效 + 星光炸开</summary>
    private void FireClink()
    {
        AudioManager.Instance?.PlaySfx(AudioManager.SfxClink, -8f);

        // 接触点：我这边右杯口和金艮左杯口的正中间
        var mineRim = myOffset + new Vector2(GlassTopW, GlassRimY);
        var friendRim = friendPos + new Vector2(-GlassTopW * FriendScale, GlassRimY * FriendScale);
        var contact = (mineRim + friendRim) * 0.5f;

        var burst = new ClinkBurst { Position = contact };
        AddChild(burst);
    }

    // ==================== 绘制 ====================

    public override void _Draw()
    {
        // ---- 桌面上的影子 ----
        DrawEllipseLocal(myOffset + new Vector2(0f, 4f), 62f, 12f, new Color(0f, 0f, 0f, 0.16f));
        DrawEllipseLocal(friendPos + new Vector2(0f, 3f), 50f, 10f, new Color(0f, 0f, 0f, 0.13f));
        DrawEllipseLocal(new Vector2(BottleDx + 8f, 4f), 40f, 10f, new Color(0f, 0f, 0f, 0.15f));

        // ---- 酒瓶（倒酒时绕瓶底旋转、并朝杯子挪一点） ----
        float angle = 0f;
        if (pourT >= 0f)
        {
            if (pourT < TiltIn)
                angle = PourAngle * EaseOutCubic(pourT / TiltIn);
            else if (pourT < TiltIn + HoldDur)
                angle = PourAngle + Mathf.Sin((pourT - TiltIn) * 9f) * 0.008f; // 微小颤动
            else
                angle = PourAngle * (1f - EaseOutCubic((pourT - TiltIn - HoldDur) / TiltOut));
        }

        float bottleShift = angle / PourAngle * 24f; // 倾斜越多，瓶身越靠向杯子
        var bottleBase = new Vector2(BottleDx + bottleShift, 0f);

        DrawSetTransform(bottleBase, angle, Vector2.One);
        DrawBottleLocal();
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // ---- 酒线（瓶口 → 杯里的酒面） ----
        if (pourT >= TiltIn * 0.75f && pourT < TiltIn + HoldDur + TiltOut * 0.3f && fillShown < 0.99f)
        {
            var mouth = bottleBase + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * BottleH;
            DrawStream(mouth);
        }

        // ---- 两只杯子 ----
        DrawSetTransform(myOffset, 0f, Vector2.One);
        DrawGlassLocal(fillShown, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        DrawSetTransform(friendPos, 0f, new Vector2(FriendScale, FriendScale));
        DrawGlassLocal(friendFillShown, false);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>在玻璃杯自己的坐标系里画（原点 = 杯底中心）</summary>
    private void DrawGlassLocal(float level, bool mine)
    {
        // 杯内底色
        var interior = new[]
        {
            new Vector2(-GlassBaseW, GlassBottomY),
            new Vector2(GlassBaseW, GlassBottomY),
            new Vector2(GlassTopW, GlassRimY),
            new Vector2(-GlassTopW, GlassRimY),
        };
        DrawColoredPolygon(interior, new Color(0.85f, 0.9f, 1f, mine ? 0.14f : 0.10f));

        // 酒面
        float surfY = GsurfaceY(level);
        if (level > 0.005f)
        {
            float hw = GhalfWidth(surfY);
            var beer = new[]
            {
                new Vector2(-GlassBaseW + 1.5f, GlassBottomY),
                new Vector2(GlassBaseW - 1.5f, GlassBottomY),
                new Vector2(hw, surfY),
                new Vector2(-hw, surfY),
            };
            DrawColoredPolygon(beer, new Color(0.84f, 0.55f, 0.16f, 0.9f));

            // 酒里的小气泡：慢慢往上冒，到酒面就没了
            for (int i = 0; i < 5; i++)
            {
                float seed = i * 1.37f;
                float span = surfY - GlassBottomY;
                if (span < 12f)
                    break;
                float by = surfY - ((time * 14f + seed * 29f) % span);
                float bx = Mathf.Sin(seed * 9.1f) * (hw - 9f) * 0.8f;
                DrawCircle(new Vector2(bx, by), 1.6f + (i % 3) * 0.5f, new Color(1f, 0.92f, 0.75f, 0.35f));
            }

            // 泡沫：酒面上一起一伏的一层
            float wob = settleT > 0f ? Mathf.Sin(settleT * 26f) * 2.2f * (settleT / SettleDur) : 0f;
            DrawEllipseLocal(new Vector2(0f, surfY + wob), hw + 1f, 6.5f, new Color(0.99f, 0.95f, 0.86f, 0.95f));
            DrawEllipseLocal(new Vector2(0f, surfY + wob + 3f), hw - 6f, 3.5f, new Color(1f, 0.99f, 0.94f, 0.7f));
        }

        // 杯壁高光（左边一道、右边一道淡的）
        // 注意顶点顺序必须守住"左下→右下→右上→左上"（2026-10-09 回归踩坑：
        // 后两点写反会成蝴蝶结自交多边形——Godot 三角化失败，每帧刷 ERROR 并拖慢整个游戏）
        DrawColoredPolygon(new[]
        {
            new Vector2(-GlassOuterW + 5f, -18f),
            new Vector2(-GlassOuterW + 11f, -18f),
            new Vector2(-GlassTopW + 3f, GlassRimY + 12f),
            new Vector2(-GlassTopW - 3f, GlassRimY + 12f),
        }, new Color(1f, 1f, 1f, 0.15f));
        DrawColoredPolygon(new[]
        {
            new Vector2(GlassOuterW - 10f, -30f),
            new Vector2(GlassOuterW - 5f, -30f),
            new Vector2(GlassTopW - 2f, GlassRimY + 30f),
            new Vector2(GlassTopW - 7f, GlassRimY + 30f),
        }, new Color(1f, 1f, 1f, 0.10f));

        // 杯底（椭圆座，让杯子"坐"在桌上）
        DrawEllipseLocal(new Vector2(0f, -2f), GlassOuterW - 2f, 9f, new Color(0.75f, 0.8f, 0.87f, 0.16f));

        // 外轮廓（酒桌上背景杂，轮廓要挺一点，杯子才"立得住"）
        DrawPolyline(new[]
        {
            new Vector2(-GlassOuterW, -6f),
            new Vector2(-GlassTopW - 5f, GlassRimY),
            new Vector2(GlassTopW + 5f, GlassRimY),
            new Vector2(GlassOuterW, -6f),
        }, new Color(0.92f, 0.95f, 0.99f, 0.8f), 3.5f);

        // 杯口（扁扁的一圈）
        DrawEllipseLocal(new Vector2(0f, GlassRimY), GlassTopW + 5f, 8f, new Color(0.9f, 0.94f, 0.98f, 0.65f), filled: false, width: 3.5f);
    }

    /// <summary>酒瓶在自己的坐标系里画（原点 = 瓶底中心，瓶口朝 -y）</summary>
    private void DrawBottleLocal()
    {
        var glass = new Color(0.13f, 0.25f, 0.18f, 0.97f);

        // 瓶身
        DrawColoredPolygon(new[]
        {
            new Vector2(-32f, 0f),
            new Vector2(32f, 0f),
            new Vector2(32f, -306f),
            new Vector2(12f, -352f),
            new Vector2(-12f, -352f),
            new Vector2(-32f, -306f),
        }, glass);

        // 瓶颈 + 瓶口
        DrawColoredPolygon(new[]
        {
            new Vector2(-12f, -352f),
            new Vector2(12f, -352f),
            new Vector2(12f, -400f),
            new Vector2(-12f, -400f),
        }, glass);
        DrawColoredPolygon(new[]
        {
            new Vector2(-15f, -407f),
            new Vector2(15f, -407f),
            new Vector2(15f, -396f),
            new Vector2(-15f, -396f),
        }, new Color(0.2f, 0.36f, 0.27f, 0.97f));
        DrawEllipseLocal(new Vector2(0f, -407f), 12f, 3.6f, new Color(0.05f, 0.08f, 0.06f, 0.9f));

        // 高光
        DrawColoredPolygon(new[]
        {
            new Vector2(-24f, -24f),
            new Vector2(-17f, -24f),
            new Vector2(-17f, -290f),
            new Vector2(-24f, -290f),
        }, new Color(1f, 1f, 1f, 0.15f));

        // 酒标（淡黄纸 + 一条红）
        DrawColoredPolygon(new[]
        {
            new Vector2(-33f, -262f),
            new Vector2(33f, -262f),
            new Vector2(33f, -158f),
            new Vector2(-33f, -158f),
        }, new Color(0.93f, 0.87f, 0.7f, 0.96f));
        DrawColoredPolygon(new[]
        {
            new Vector2(-33f, -208f),
            new Vector2(33f, -208f),
            new Vector2(33f, -186f),
            new Vector2(-33f, -186f),
        }, new Color(0.72f, 0.27f, 0.2f, 0.95f));
    }

    /// <summary>酒线：从瓶口到酒面的一条细流（带一点弧度）</summary>
    private void DrawStream(Vector2 mouth)
    {
        float surfY = GsurfaceY(fillShown);
        var land = new Vector2(Mathf.Lerp(mouth.X, 6f, 0.75f), surfY - 4f);
        var ctrl = new Vector2((mouth.X + land.X) * 0.5f + 14f, (mouth.Y + land.Y) * 0.5f);

        var pts = new Vector2[9];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)(pts.Length - 1);
            pts[i] = (1 - t) * (1 - t) * mouth + 2 * (1 - t) * t * ctrl + t * t * land;
        }

        DrawPolyline(pts, new Color(0.95f, 0.72f, 0.32f, 0.5f), 6f);
        DrawPolyline(pts, new Color(1f, 0.95f, 0.8f, 0.85f), 2.6f);
    }

    /// <summary>画一个扁椭圆（中心 + 半径，直接落在当前坐标系里）</summary>
    private void DrawEllipseLocal(Vector2 center, float rx, float ry, Color color, bool filled = true, float width = 2f)
    {
        const int N = 26;
        var pts = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = Mathf.Tau * i / N;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        if (filled)
            DrawColoredPolygon(pts, color);
        else
            DrawPolyline(pts, color, width);
    }

    /// <summary>酒面高度：level 0 → 杯内底部，1 → 杯口下缘</summary>
    private static float GsurfaceY(float level) =>
        Mathf.Lerp(GlassBottomY, GlassRimY + 6f, Mathf.Clamp(level, 0f, 1f));

    /// <summary>杯内某个高度处的半宽（杯壁是斜的）</summary>
    private static float GhalfWidth(float y)
    {
        float t = Mathf.Clamp((GlassBottomY - y) / (GlassBottomY - GlassRimY), 0f, 1f);
        return Mathf.Lerp(GlassBaseW - 1.5f, GlassTopW - 2f, t);
    }
}

/// <summary>碰杯那一下的星光炸开（自己画、自己消失）</summary>
public partial class ClinkBurst : Node2D
{
    private const float Dur = 0.55f;
    private float t;
    private readonly List<(float Ang, float Len)> rays = new();
    private readonly Random rng = new();

    public override void _Ready()
    {
        ZIndex = 5;
        for (int i = 0; i < 10; i++)
            rays.Add((Mathf.Tau * i / 10f + (float)rng.NextDouble() * 0.35f, 13f + (float)rng.NextDouble() * 18f));
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t >= Dur)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float p = t / Dur;
        float a = 1f - p;

        DrawArc(Vector2.Zero, 12f + 46f * p, 0f, Mathf.Tau, 30, new Color(1f, 0.9f, 0.6f, a * 0.55f), 2.6f);

        foreach (var (ang, len) in rays)
        {
            var dir = Vector2.Right.Rotated(ang);
            float r0 = 14f + 34f * p;
            DrawLine(dir * r0, dir * (r0 + len * (1f - p * 0.5f)), new Color(1f, 0.95f, 0.75f, a * 0.9f), 2.4f);
        }

        DrawCircle(Vector2.Zero, 7f * (1f - p) + 2.2f, new Color(1f, 0.98f, 0.9f, a));
    }
}

/// <summary>烤炉上空的火星子：慢慢飘、轻轻摇、一小会儿就没了</summary>
public partial class EmberDust : Node2D
{
    private class Ember
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public float Sway;
    }

    private readonly List<Ember> list = new();
    private readonly Random rng = new();
    private float acc;

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 攒着生成：每秒几颗，最多同时飘 26 颗
        acc += dt * 6f;
        while (acc >= 1f)
        {
            acc -= 1f;
            if (list.Count < 26)
                Spawn();
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var e = list[i];
            e.Life += dt;
            if (e.Life >= e.MaxLife)
            {
                list.RemoveAt(i);
                continue;
            }
            e.Pos += e.Vel * dt;
            e.Pos.X += Mathf.Sin(e.Life * 2.2f + e.Sway) * 9f * dt; // 轻轻摇摆
            e.Vel.Y -= 5f * dt; // 越飘越轻
        }

        QueueRedraw();
    }

    private void Spawn()
    {
        list.Add(new Ember
        {
            Pos = new Vector2((float)(rng.NextDouble() * 2 - 1) * 150f, (float)(rng.NextDouble() * 30 - 8)),
            Vel = new Vector2((float)(rng.NextDouble() * 2 - 1) * 10f, -(18f + (float)rng.NextDouble() * 24f)),
            MaxLife = 1.5f + (float)rng.NextDouble() * 1.7f,
            Size = 1.9f + (float)rng.NextDouble() * 2.4f,
            Sway = (float)rng.NextDouble() * Mathf.Tau,
        });
    }

    public override void _Draw()
    {
        foreach (var e in list)
        {
            float p = e.Life / e.MaxLife;
            float a = (p < 0.25f ? p / 0.25f : 1f - (p - 0.25f) / 0.75f) * 0.95f;
            DrawCircle(e.Pos, e.Size, new Color(1f, 0.62f, 0.3f, a));
            DrawCircle(e.Pos, e.Size * 0.45f, new Color(1f, 0.9f, 0.6f, a * 0.9f));
        }
    }
}
