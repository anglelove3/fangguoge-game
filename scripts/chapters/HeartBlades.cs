using Godot;
using System.Collections.Generic;

/// <summary>
/// 心脏场景（第三轮：《天平》章结尾的心理间奏）
///
/// 她说完「对不起」，走出咖啡馆之后，画面暗下来——不是黑场，是像闭上眼睛。
/// 黑暗里有一颗心在跳。然后她说过的话变成四把刀，一把一把扎进来：
/// 没安全感 / 没时间陪 / 永远不在 / 好闷。
///
/// 每插一把：画面震一下，褐色的旧血从刀口渗开，心跳漏一拍。
/// 四把刀插满后，玩家可以点刀把它拔出来——也可以不拔。
/// 无论拔不拔，那张字都会出现：
///
///     「它还在跳。」
///
/// 这是全书「放过自己」最初的伏笔。
/// </summary>
public partial class HeartBlades : Control
{
    /// <summary>演出结束（拔了几把刀通过参数带回）</summary>
    private readonly System.Action<int> onDone;

    private Texture2D heart;
    private Vector2 heartCenter;
    private float heartScale;

    private float t;
    private const float DarkIn = 0.9f;
    private const float HeartIn = 1.4f;
    private const float BladeInterval = 1.45f;
    private const float BladeFlyIn = 0.42f;
    private const float WaitAfterBlades = 9f;    // 插满后等待（可拔刀；不管也会自己往下走）
    private const float LineShow = 2.6f;
    private const float FadeOut = 0.8f;

    private static readonly string[] Blame =
    {
        "你给不了我安全感。",
        "你从来没时间陪我。",
        "我难受的时候，你永远不在。",
        "跟你在一起，好闷。",
    };

    private class Blade
    {
        public Vector2 Pos;        // 插在心脏上的位置（屏幕坐标）
        public Vector2 Dir;        // 刀尖朝向（用于画刀身）
        public float Angle;        // 刀身角度
        public float InT;          // 飞入进度 0~1
        public float AppearAt;     // 何时开始飞入
        public Vector2 StartFrom;  // 从哪个屏幕外点飞来
        public string Text = "";
        public float TextA;        // 台词透明度
        public bool Pulled;        // 已拔
        public bool Pulling;
        public float PullT;
    }

    private readonly List<Blade> blades = new();
    private readonly List<(Vector2 p, float r, float a)> blood = new();
    private int pulledCount;
    private bool allIn;
    private float clickWindow;
    private int clickCount;
    private bool fifthFired;
    private float fifthShake;
    private bool lineShown;
    private float shakeT;
    private float beatSkip;      // 漏拍（0~1，1=刚漏过）

    private readonly bool skipWorstBlade;

    /// <param name="skipWorstBlade">true = 她说不出口最狠那句（玩家收回过钥匙）</param>
    public HeartBlades(System.Action<int> done, bool skipWorstBlade = false)
    {
        onDone = done;
        this.skipWorstBlade = skipWorstBlade;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        heart = ResourceLoader.Exists("res://assets/art/props/heart_v1.png")
            ? GD.Load<Texture2D>("res://assets/art/props/heart_v1.png") : null;

        var size = GetViewportRect().Size;
        heartCenter = size / 2f;
        heartScale = Mathf.Min(size.Y * 0.52f, size.X * 0.34f) / (heart?.GetHeight() ?? 900f);

        // 刀：绕心脏边缘四个方向飞入（避开正中）
        var dirs = new[]
        {
            (new Vector2(-1f, -0.35f), 0.55f),
            (new Vector2(1f, -0.15f), -0.35f),
            (new Vector2(-0.85f, 0.45f), -0.25f),
            (new Vector2(0.9f, 0.4f), 0.65f),
        };
        var rng = new System.Random(2026);
        int count = skipWorstBlade ? 3 : 4;
        for (int i = 0; i < count; i++)
        {
            var dir = dirs[i].Item1.Normalized();
            var pos = heartCenter + dir * (heartScale * 983f * 0.12f);
            var from = heartCenter + dir * (size.Length() * 0.75f);
            blades.Add(new Blade
            {
                Pos = pos,
                Dir = dir,
                Angle = dirs[i].Item2,
                AppearAt = DarkIn + HeartIn + i * BladeInterval,
                StartFrom = from,
                Text = Blame[i],
            });
        }
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float dt = (float)delta;

        // 插刀推进
        foreach (var b in blades)
        {
            if (t >= b.AppearAt && b.InT < 1f)
            {
                float before = b.InT;
                b.InT = Mathf.Clamp((t - b.AppearAt) / BladeFlyIn, 0f, 1f);
                if (before < 1f && b.InT >= 1f)
                {
                    // 落刀瞬间：震动 + 漏拍 + 血渗开
                    shakeT = 0.32f;
                    beatSkip = 1f;
                    blood.Add((b.Pos, 0f, 0f));
                    AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -8f, 0.05f, 0.75f);
                }
            }
            // 台词：落刀后淡入，停 2 秒淡出
            float since = t - (b.AppearAt + BladeFlyIn);
            if (since > 0f && !b.Pulled)
            {
                b.TextA = since < 0.35f ? since / 0.35f
                    : since < 2.4f ? 1f
                    : Mathf.Max(0f, 1f - (since - 2.4f) / 0.7f);
            }
            else
            {
                b.TextA = 0f;
            }
            // 拔刀动画
            if (b.Pulling)
            {
                b.PullT += dt / 0.55f;
                if (b.PullT >= 1f)
                {
                    b.Pulling = false;
                    b.Pulled = true;
                    pulledCount++;
                    blood.Add((b.Pos, 0f, 0f));
                    beatSkip = 1f;
                    shakeT = 0.25f;
                }
            }
        }

        // 血缓慢扩散
        for (int i = 0; i < blood.Count; i++)
        {
            var (p, r, a) = blood[i];
            blood[i] = (p, Mathf.Min(r + dt * 34f, 90f), Mathf.Min(a + dt * 1.6f, 0.55f));
        }

        if (clickWindow > 0f)
        {
            clickWindow -= dt;
            if (clickWindow <= 0f)
                clickCount = 0;
        }
        if (fifthShake > 0f)
            fifthShake = Mathf.Max(0f, fifthShake - dt);

        if (shakeT > 0f)
            shakeT = Mathf.Max(0f, shakeT - dt);
        if (beatSkip > 0f)
            beatSkip = Mathf.Max(0f, beatSkip - dt * 1.4f);

        // 第三把刀（或最后一把）落下时 BGM 停（音乐骤停）
        int bgmStopAt = Mathf.Min(2, blades.Count - 1);
        if (t >= blades[bgmStopAt].AppearAt + BladeFlyIn - dt && t < blades[bgmStopAt].AppearAt + BladeFlyIn)
            AudioManager.Instance?.PlayBgm("", 0f, 0.4f);

        allIn = t >= blades[blades.Count - 1].AppearAt + BladeFlyIn;

        // 结束
        float endAt = blades[blades.Count - 1].AppearAt + BladeFlyIn + WaitAfterBlades;
        if (!lineShown && t >= endAt)
        {
            lineShown = true;
            AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -22f, 0.05f);
        }
        if (t >= endAt + LineShow + FadeOut)
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke(pulledCount);
            return;
        }
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!(@event is InputEventMouseButton mb) || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;

        // 第三轮：不耐烦是有代价的——插刀过程中狂点，她自己再补一刀
        clickCount++;
        clickWindow = 0.6f;
        if (!fifthFired && !allIn && clickCount >= 4 && t > DarkIn + HeartIn + 1.0f)
        {
            SendFifthBlade();
            return;
        }

        if (!allIn)
            return;
        var p = GetViewport().GetMousePosition();
        foreach (var b in blades)
        {
            if (b.Pulled || b.Pulling)
                continue;
            if (p.DistanceTo(b.Pos) < 52f)
            {
                b.Pulling = true;
                AudioManager.Instance?.PlaySfx(AudioManager.SfxScaleCreak, -12f, 0.05f, 1.5f);
                break;
            }
        }
    }

    /// <summary>第五把刀：你连听她说完都做不到（玩家自己招来的）</summary>
    private void SendFifthBlade()
    {
        fifthFired = true;
        fifthShake = 0.5f;
        var from = heartCenter + new Vector2(0, -GetViewportRect().Size.Y * 0.7f);
        blades.Add(new Blade
        {
            Pos = heartCenter + new Vector2(0, heartScale * 983f * 0.06f),
            Dir = new Vector2(0, 1),
            Angle = 0.05f,
            AppearAt = t,
            StartFrom = from,
            Text = "你连听我说完，都做不到。",
        });
        AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -4f, 0.05f, 0.6f);
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var shake = shakeT > 0f
            ? new Vector2((float)GD.RandRange(-4, 4), (float)GD.RandRange(-3, 3)) * (shakeT / 0.32f)
            : Vector2.Zero;

        // 黑底（闭眼后的黑暗，不是纯黑——带一点暖褐）
        float dark = Mathf.Clamp(t / DarkIn, 0f, 1f);
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.055f, 0.035f, 0.03f, dark));

        if (heart == null)
            return;

        // 第三轮反馈 6：背景里几条极淡的血管线（缓慢流动的曲线）
        for (int i = 0; i < 5; i++)
        {
            float ph = t * 0.15f + i * 1.3f;
            var a0 = heartCenter + new Vector2(-size.X * 0.45f, (i - 2) * 90f + Mathf.Sin(ph) * 30f);
            var a1 = heartCenter + new Vector2(0, (i - 2) * 60f + Mathf.Cos(ph * 1.2f) * 40f);
            var a2 = heartCenter + new Vector2(size.X * 0.45f, (i - 2) * 80f + Mathf.Sin(ph * 0.8f) * 34f);
            DrawPolyline(new[] { a0, a1, a2 }, new Color(0.35f, 0.14f, 0.12f, 0.10f), 1.4f);
        }

        // 心跳波纹：每一次搏动从心脏向外扩一圈
        if (t > DarkIn + 0.4f)
        {
            float period = 0.84f;
            float ph2 = (t % period) / period;
            if (ph2 < 0.45f)
            {
                float k = ph2 / 0.45f;
                float rr = (heartScale * 983f * 0.34f) * (0.6f + k * 1.1f);
                DrawArc(heartCenter + shake, rr, 0, Mathf.Tau, 48,
                    new Color(0.55f, 0.22f, 0.18f, (1f - k) * 0.16f), 2.2f);
            }
        }

        // 漂浮的细小尘屑（慢慢往上飘，像水里的杂质）
        for (int i = 0; i < 26; i++)
        {
            float seed = i * 53.7f;
            float px = heartCenter.X + Mathf.Sin(seed + t * 0.3f) * (size.X * 0.30f + (seed % 7) * 18f);
            float py = heartCenter.Y + Mathf.Cos(seed * 1.7f + t * 0.22f) * (size.Y * 0.26f + (seed % 5) * 22f)
                       - ((t * 8f + seed) % 40f);
            float aa = 0.05f + 0.06f * (0.5f + 0.5f * Mathf.Sin(seed + t));
            DrawCircle(new Vector2(px, py), 1.4f + (seed % 3) * 0.5f,
                new Color(0.62f, 0.55f, 0.48f, aa));
        }

        // 心脏：搏动（72bpm 左右；漏拍时停一下）
        float beat = 1f;
        if (t > DarkIn + 0.4f)
        {
            float period = 0.84f;
            float phase = (t % period) / period;
            float pulse = Mathf.Sin(phase * Mathf.Pi) * (phase < 0.35f ? 1f : 0.25f);
            beat = 1f + 0.022f * pulse * (1f - beatSkip);
        }
        float appear = Mathf.Clamp((t - DarkIn) / HeartIn, 0f, 1f);
        float s = heartScale * beat;

        var hpos = heartCenter - new Vector2(heart.GetWidth(), heart.GetHeight()) * s / 2f + shake;
        DrawTextureRect(heart, new Rect2(hpos, heart.GetSize() * s), false,
            new Color(1f, 1f, 1f, appear * 0.95f));

        // 血（旧血褐色，缓慢渗开在刀口周围）
        foreach (var (p, r, a) in blood)
        {
            if (r <= 0.5f)
                continue;
            DrawCircle(p + shake, r, new Color(0.32f, 0.10f, 0.07f, a * 0.55f));
            DrawCircle(p + shake, r * 0.55f, new Color(0.24f, 0.07f, 0.05f, a * 0.6f));
        }

        // 刀
        foreach (var b in blades)
        {
            if (b.InT <= 0f)
                continue;
            if (b.Pulled)
                continue;

            Vector2 pos = b.Pos + shake;
            if (b.InT < 1f)
            {
                // 飞入：从 StartFrom 缓动到 Pos（Back ease in）
                float e = 1f - Mathf.Pow(1f - b.InT, 2.4f);
                pos = b.StartFrom.Lerp(b.Pos, e) + shake;
            }
            // 拔出：沿刀身方向往外滑 + 淡出
            float alpha = 1f;
            if (b.Pulling)
            {
                float e = b.PullT * b.PullT;
                pos += b.Dir * (120f * e);
                alpha = 1f - e * 0.85f;
            }

            DrawSetTransform(pos, b.Angle, Vector2.One);
            // 刀身（细长冷青灰）
            // 第三轮反馈 6：刀改极简剪影——一整片深灰，只在刃口留一道细高光，
            // 和心脏的水彩质感不再打架
            DrawColoredPolygon(new[]
            {
                new Vector2(0, 8),
                new Vector2(-16, -90),
                new Vector2(0, -98),
                new Vector2(16, -90),
            }, new Color(0.10f, 0.11f, 0.13f, alpha * 0.92f));
            DrawLine(new Vector2(1, 4), new Vector2(11, -84), new Color(0.72f, 0.76f, 0.82f, alpha * 0.55f), 1.4f);
            // 刀柄（一段深色，简单）
            DrawRect(new Rect2(-9, -160, 18, 62), new Color(0.14f, 0.12f, 0.11f, alpha));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            // 台词（刀柄旁边）
            if (b.TextA > 0.01f)
            {
                var font = GetThemeDefaultFont();
                float tw = font.GetStringSize(b.Text, HorizontalAlignment.Left, -1, 26).X;
                var tp = b.Pos + new Vector2(b.Dir.X > 0 ? 46f : -tw - 46f, -186f) + shake;
                DrawString(font, tp, b.Text, HorizontalAlignment.Left, -1, 26,
                    new Color(0.90f, 0.86f, 0.80f, b.TextA));
            }
        }

        // 「点刀可以拔出来」提示（插满后、还没拔的时候）
        if (allIn && pulledCount == 0 && t < blades[blades.Count - 1].AppearAt + BladeFlyIn + 6f)
        {
            var font = GetThemeDefaultFont();
            string hint = "（点那些刀，可以拔出来。）";
            var hp = new Vector2(size.X / 2f - 160f, size.Y * 0.88f);
            DrawString(font, hp, hint, HorizontalAlignment.Left, -1, 22,
                new Color(0.75f, 0.72f, 0.68f, 0.75f));
        }

        // 「它还在跳。」
        if (lineShown)
        {
            float a = Mathf.Clamp((t - (blades[blades.Count - 1].AppearAt + BladeFlyIn + WaitAfterBlades)) / 1.2f, 0f, 1f);
            var font = GetThemeDefaultFont();
            var lp = new Vector2(size.X / 2f - 62f, size.Y * 0.32f);
            DrawString(font, lp, "它还在跳。", HorizontalAlignment.Left, -1, 34,
                new Color(0.86f, 0.80f, 0.74f, a));
        }
    }
}
