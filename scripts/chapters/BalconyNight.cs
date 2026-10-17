using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 阳台夜（第二十轮 F8）：第十章深夜，选了「去阳台吹十分钟风」之后的全屏演出。
///
/// 【画面】夜空压底 → 远处宿舍楼一格格亮着的窗 → 阳台栏杆 → 男主剪影
///         （一手搭栏杆，一手夹烟）→ 烟头红点呼吸 → 烟雾一缕缕往上飘。
///
/// 【为什么纯代码画】
/// 和 EmberDust / DrinkPour 同一套思路：剪影本来就是黑的，几何形+粒子最干净，
/// 不用美术图，窗口怎么缩都不糊。烟雾粒子和主菜单咖啡蒸汽是同一套写法。
///
/// 【用法】Ch10 在 draft_2 对白期间挂上（FadeIn），对白播完 FadeOut 后自毁。
/// 对话 UI 是 CanvasLayer(50)，永远盖在这一层上面——对白和夜风同屏。
/// </summary>
public partial class BalconyNight : Control
{
    // ---------- 烟雾粒子 ----------
    private class Smoke
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public float Sway;
    }

    private readonly List<Smoke> smokes = new();
    private readonly Random rng = new();
    private float smokeAcc;
    private float time;

    // ---------- 画面几何（相对坐标，_Draw 里按 Size 换算） ----------

    /// <summary>男主剪影锚点（x 比例，y 为地面线比例）</summary>
    private const float ManX = 0.66f;
    private const float GroundY = 0.88f;

    /// <summary>烟头局部偏移（相对人物锚点，屏幕像素、按 1080 高基准缩放）</summary>
    private static readonly Vector2 CigaretteOffset = new(-14f, -132f);

    private float fadeIn;      // 0→1 淡入进度（也当烟雾发射的开关用）

    /// <summary>淡入（对白开始的同时夜色漫上来）</summary>
    public void FadeIn(float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 1f, seconds);
    }

    /// <summary>淡出后自毁（回屋）</summary>
    public void FadeOut(float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, seconds);
        tw.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;
        fadeIn = Mathf.Min(1f, fadeIn + dt / 0.9f);

        // 攒烟：每 0.16 秒一颗，最多 40 颗同时在飘
        smokeAcc += dt * 6.2f;
        while (smokeAcc >= 1f)
        {
            smokeAcc -= 1f;
            if (smokes.Count < 40)
                SpawnSmoke();
        }

        for (int i = smokes.Count - 1; i >= 0; i--)
        {
            var s = smokes[i];
            s.Life += dt;
            if (s.Life >= s.MaxLife)
            {
                smokes.RemoveAt(i);
                continue;
            }
            s.Pos += s.Vel * dt;
            s.Pos.X += Mathf.Sin(s.Life * 1.8f + s.Sway) * 7f * dt; // 轻轻摇
            s.Vel.Y -= 2.5f * dt;                                   // 越飘越慢地加速上浮
        }

        QueueRedraw();
    }

    private void SpawnSmoke()
    {
        var c = CigarettePoint();
        smokes.Add(new Smoke
        {
            Pos = c + new Vector2((float)(rng.NextDouble() - 0.5) * 6f, 0),
            Vel = new Vector2((float)(rng.NextDouble() - 0.5) * 6f, -(22f + (float)rng.NextDouble() * 14f)),
            MaxLife = 2.6f + (float)rng.NextDouble() * 1.8f,
            Size = 4f + (float)rng.NextDouble() * 3f,
            Sway = (float)rng.NextDouble() * Mathf.Tau,
        });
    }

    /// <summary>烟头的屏幕位置（和 _Draw 里的人物几何共用一套换算）</summary>
    private Vector2 CigarettePoint()
    {
        float unit = Size.Y / 1080f; // 按 1080p 基准等比
        var anchor = new Vector2(Size.X * ManX, Size.Y * GroundY);
        return anchor + CigaretteOffset * unit;
    }

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        float unit = h / 1080f;

        // ---- 夜空：上深下微亮（远处的城市光） ----
        DrawColoredPolygon(new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) },
            new Color(0.045f, 0.055f, 0.09f));
        DrawColoredPolygon(new[]
        {
            new Vector2(0, h * 0.55f), new Vector2(w, h * 0.55f), new Vector2(w, h), new Vector2(0, h),
        }, new Color(0.06f, 0.075f, 0.115f));

        // 一弯月牙（右上，很淡）
        var moonC = new Vector2(w * 0.18f, h * 0.16f);
        DrawCircle(moonC, 34 * unit, new Color(0.92f, 0.90f, 0.82f, 0.5f));
        DrawCircle(moonC + new Vector2(14 * unit, -8 * unit), 30 * unit, new Color(0.045f, 0.055f, 0.09f));

        // ---- 远处宿舍楼（三栋，窗灯一格一格；固定种子，不闪） ----
        var buildings = new (float X, float W, float Top, int Cols, int Rows, int Seed)[]
        {
            (0.02f, 0.22f, 0.42f, 6, 9, 11),
            (0.30f, 0.26f, 0.34f, 7, 11, 23),
            (0.62f, 0.24f, 0.46f, 6, 9, 37),
        };
        foreach (var b in buildings)
        {
            var bodyRect = new Rect2(w * b.X, h * b.Top, w * b.W, h - h * b.Top);
            DrawRect(bodyRect, new Color(0.032f, 0.04f, 0.065f));

            var winRect = bodyRect.GrowIndividual(14 * unit, 18 * unit, 14 * unit, 20 * unit);
            float cw = winRect.Size.X / b.Cols;
            float ch = winRect.Size.Y / b.Rows;
            var r = new Random(b.Seed);
            for (int cx = 0; cx < b.Cols; cx++)
            {
                for (int cy = 0; cy < b.Rows; cy++)
                {
                    if (r.NextDouble() > 0.30)
                        continue; // 大部分窗是黑的
                    var winPos = new Vector2(winRect.Position.X + cx * cw + cw * 0.22f,
                        winRect.Position.Y + cy * ch + ch * 0.24f);
                    var winSize = new Vector2(cw * 0.56f, ch * 0.5f);
                    // 暖黄窗灯，深浅随缘
                    float warm = 0.30f + (float)r.NextDouble() * 0.30f;
                    DrawRect(new Rect2(winPos, winSize), new Color(0.95f, 0.78f, 0.45f, warm * fadeIn));
                }
            }
        }

        // ---- 阳台地面 ----
        DrawRect(new Rect2(0, h * GroundY, w, h * (1f - GroundY)), new Color(0.05f, 0.055f, 0.085f));

        // ---- 栏杆（横杆 + 竖杆），在人物身前 ----
        float railY = h * (GroundY - 0.24f);
        var railColor = new Color(0.06f, 0.068f, 0.10f);
        DrawRect(new Rect2(0, railY, w, 7 * unit), railColor);
        DrawRect(new Rect2(0, h * GroundY - 26 * unit, w, 8 * unit), railColor);
        for (float px = w * 0.04f; px < w; px += w * 0.07f)
            DrawRect(new Rect2(px, railY, 4.5f * unit, h * GroundY - 26 * unit - railY), new Color(0.055f, 0.062f, 0.095f));

        // ---- 男主剪影 ----
        var ink = new Color(0.035f, 0.04f, 0.06f);
        var man = new Vector2(w * ManX, h * GroundY);

        // 头 + 颈肩身（微侧向左，看着远处的楼）
        DrawCircle(man + new Vector2(-4 * unit, -150 * unit), 21 * unit, ink);
        DrawColoredPolygon(new[]
        {
            new Vector2(man.X - 26 * unit, man.Y - 124 * unit),
            new Vector2(man.X + 20 * unit, man.Y - 124 * unit),
            new Vector2(man.X + 30 * unit, man.Y - 30 * unit),
            new Vector2(man.X - 32 * unit, man.Y - 30 * unit),
        }, ink);

        // 右臂：搭在栏杆上
        DrawColoredPolygon(new[]
        {
            new Vector2(man.X + 12 * unit, man.Y - 112 * unit),
            new Vector2(man.X + 30 * unit, man.Y - 104 * unit),
            new Vector2(man.X + 46 * unit, railY - 4 * unit),
            new Vector2(man.X + 30 * unit, railY - 2 * unit),
        }, ink);

        // 左臂：抬到脸边，夹着烟
        DrawColoredPolygon(new[]
        {
            new Vector2(man.X - 16 * unit, man.Y - 112 * unit),
            new Vector2(man.X - 2 * unit, man.Y - 106 * unit),
            new Vector2(man.X - 16 * unit, man.Y - 138 * unit),
            new Vector2(man.X - 26 * unit, man.Y - 132 * unit),
        }, ink);

        // 腿
        DrawRect(new Rect2(man.X - 26 * unit, man.Y - 34 * unit, 16 * unit, 34 * unit), ink);
        DrawRect(new Rect2(man.X + 4 * unit, man.Y - 34 * unit, 16 * unit, 34 * unit), ink);

        // ---- 烟头：红点呼吸（吸气时亮一下） ----
        var cig = CigarettePoint();
        float breath = 0.5f + 0.5f * Mathf.Sin(time * 3.4f); // 约 1.85 秒一个呼吸周期
        DrawCircle(cig, 3.2f * unit, new Color(0.25f, 0.22f, 0.2f));
        DrawCircle(cig, (2.2f + 1.6f * breath) * unit,
            new Color(1f, 0.42f, 0.25f, 0.55f + 0.45f * breath));
        DrawCircle(cig, 1.4f * unit, new Color(1f, 0.85f, 0.6f, 0.6f + 0.4f * breath));

        // ---- 烟雾（烟头往上，一圈淡灰，越飘越大越淡） ----
        foreach (var s in smokes)
        {
            float p = s.Life / s.MaxLife;
            float a = (p < 0.2f ? p / 0.2f : 1f - (p - 0.2f) / 0.8f) * 0.30f;
            float size = s.Size * (1f + p * 2.4f);
            DrawCircle(s.Pos, size, new Color(0.75f, 0.78f, 0.82f, a));
        }
    }
}
