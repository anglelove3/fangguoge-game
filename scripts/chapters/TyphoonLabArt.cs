using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第十六章·台风天的实验室（程序绘制，1792x1024 设计稿比例，按实际视口缩放）
///
/// 【为什么程序画】
/// ImageGen 服务从 10-18 起时通时不通，第三章天平、第十二章骑行、第十五章网咖都是这么过来的。
/// 台风的母题是"灰蓝 + 斜雨 + 摇树 + 屋里一盏灯"，_Draw 完全够用，还不吃积分。
///
/// 【三个可调属性（Ch16.cs 用 Tween 推）】
///   Storm  0~1  台风强度：雨量与树摆幅；尾声推到 0，雨停
///   Clear  0~1  天色：0 = 台风灰蓝，1 = 过境后的晴蓝（尾声推到 1）
///   Dim    0~1  整体压暗：深夜 0.55，尾声降到 0
///
/// 绘制层次：天空（渐变）→ 云 → 窗外树剪影（按 Storm 摇）→ 窗框 → 雨线 → 玻璃水珠
///          → 室内：桌沿 / 显示器（屏光）→ 台灯 → Dim 遮罩
/// </summary>
public partial class TyphoonLabArt : Control
{
    // ==================== 可调属性 ====================

    private float storm = 1f;
    public float Storm
    {
        get => storm;
        set { storm = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
    }

    private float clear;
    public float Clear
    {
        get => clear;
        set { clear = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
    }

    private float dim;
    public float Dim
    {
        get => dim;
        set { dim = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
    }

    /// <summary>一次设置三个天气参数（自检/调试用；剧情里用 Tween 单独推）</summary>
    public void SetWeather(float storm, float clear, float dim)
    {
        Storm = storm;
        Clear = clear;
        Dim = dim;
    }

    // ==================== 内部状态 ====================

    private class Drop
    {
        public Vector2 Pos;
        public float R;
        public float Speed;
        public float SlideIn;
        public bool Sliding;
        public float Trail;
    }

    private readonly List<Drop> drops = new();
    private readonly Random rng = new(19);
    private float t;
    private Vector2 size;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        size = GetViewportRect().Size;
        for (int i = 0; i < 42; i++)
        {
            drops.Add(new Drop
            {
                Pos = new Vector2((float)rng.NextDouble() * size.X, (float)rng.NextDouble() * size.Y * 0.7f),
                R = 1.4f + (float)rng.NextDouble() * 3.0f,
                Speed = 0f,
                SlideIn = 2f + (float)rng.NextDouble() * 12f,
            });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        t += dt;
        // 雨停（Storm→0）后就别再逐帧重绘了，省 CPU
        if (Storm > 0.02f)
        {
            foreach (var d in drops)
            {
                if (!d.Sliding)
                {
                    d.SlideIn -= dt;
                    if (d.SlideIn <= 0f)
                    {
                        d.Sliding = true;
                        d.Speed = 16f + (float)rng.NextDouble() * 30f;
                    }
                }
                else
                {
                    d.Speed += 34f * dt;
                    d.Pos.Y += d.Speed * dt;
                    d.Trail = Mathf.Min(d.Trail + dt * 96f, 64f);
                    if (d.Pos.Y > size.Y * 0.72f)
                    {
                        d.Pos = new Vector2((float)rng.NextDouble() * size.X, -10f);
                        d.Speed = 0f;
                        d.Sliding = false;
                        d.SlideIn = 1.5f + (float)rng.NextDouble() * 10f;
                        d.Trail = 0f;
                    }
                }
            }
            QueueRedraw();
        }
    }

    // ==================== 绘制 ====================

    public override void _Draw()
    {
        float w = size.X, h = size.Y;
        float skyH = h * 0.72f;   // 窗台线

        // ---- 天空（台风灰蓝 → 过境晴蓝，按 Clear 混合）----
        var stormTop = new Color(0.235f, 0.275f, 0.340f);
        var stormBot = new Color(0.420f, 0.475f, 0.530f);
        var clearTop = new Color(0.455f, 0.620f, 0.760f);
        var clearBot = new Color(0.760f, 0.840f, 0.860f);
        var top = stormTop.Lerp(clearTop, Clear);
        var bot = stormBot.Lerp(clearBot, Clear);
        DrawPolygon(
            new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, skyH), new Vector2(0, skyH) },
            new[] { top, top, bot, bot });

        // ---- 云（台风时厚，晴时薄）----
        float cloudA = Mathf.Lerp(0.30f, 0.10f, Clear);
        for (int i = 0; i < 4; i++)
        {
            float cy = h * (0.06f + i * 0.075f);
            float drift = Mathf.Sin(t * 0.08f + i) * 24f;
            float cw = w * (0.5f + 0.1f * i);
            var cloud = new Color(0.16f, 0.19f, 0.24f, cloudA * (1f - Clear));
            DrawRect(new Rect2(-40f + drift, cy, cw, h * 0.035f), cloud);
            DrawRect(new Rect2(w * 0.3f - drift * 0.6f, cy + h * 0.018f, cw * 0.8f, h * 0.028f), cloud);
        }

        // ---- 窗外的树（梧桐剪影，按 Storm 摇；Clear 高了就淡出）----
        DrawTree(w * 0.16f, skyH + 8f, h * 0.46f, Mathf.Lerp(0.85f, 0.35f, Storm * 0.4f));

        // ---- 窗框：外框 + 中竖棂 + 横棂 ----
        var frame = new Color(0.135f, 0.150f, 0.175f);
        DrawRect(new Rect2(0, 0, w, h * 0.035f), frame);                    // 上框
        DrawRect(new Rect2(0, skyH - h * 0.022f, w, h * 0.022f), frame);    // 窗台横框
        DrawRect(new Rect2(w * 0.485f, 0, w * 0.03f, skyH), frame);         // 中竖
        DrawRect(new Rect2(0, h * 0.36f, w, h * 0.022f), frame);            // 横棂
        DrawRect(new Rect2(0, 0, w * 0.02f, skyH), frame);                  // 左框
        DrawRect(new Rect2(w * 0.98f, 0, w * 0.02f, skyH), frame);          // 右框

        // ---- 雨：斜线（雨停就没了）----
        if (Storm > 0.02f)
        {
            int count = (int)(90 * Storm);
            float ang = -0.45f; // 斜度
            for (int i = 0; i < count; i++)
            {
                float seed = i * 47.3f;
                float x = (seed * 3.7f + t * 300f) % (w + 240f) - 120f;
                float y = (seed * 9.1f + t * 720f) % (skyH + 160f) - 80f;
                var c = new Color(0.82f, 0.87f, 0.92f, 0.13f * Storm);
                DrawLine(new Vector2(x, y), new Vector2(x + Mathf.Cos(ang) * 34f, y - Mathf.Sin(ang) * 34f), c, 1.4f);
            }

            // ---- 玻璃水珠 ----
            foreach (var d in drops)
            {
                if (d.Pos.Y > skyH)
                    continue;
                if (d.Sliding && d.Trail > 1f)
                    DrawLine(new Vector2(d.Pos.X, d.Pos.Y - d.Trail), d.Pos,
                        new Color(0.85f, 0.90f, 0.95f, 0.10f), d.R * 0.9f);
                DrawCircle(d.Pos, d.R, new Color(0.88f, 0.92f, 0.96f, 0.16f * Storm));
                DrawCircle(d.Pos + new Vector2(-d.R * 0.3f, -d.R * 0.3f), d.R * 0.35f,
                    new Color(1f, 1f, 1f, 0.20f * Storm));
            }
        }

        // ---- 室内：窗台下的实验桌 ----
        var desk = new Color(0.215f, 0.180f, 0.150f);
        DrawRect(new Rect2(0, skyH, w, h - skyH), desk);
        // 桌面边缘高光
        DrawRect(new Rect2(0, skyH, w, 3f), new Color(0.42f, 0.36f, 0.28f, 0.8f));

        // ---- 显示器（偏左，屏还亮着）----
        float mx = w * 0.30f, my = h * 0.78f, mw = w * 0.20f, mh = h * 0.16f;
        DrawRect(new Rect2(mx - 6f, my - 6f, mw + 12f, mh + 12f), new Color(0.08f, 0.08f, 0.09f));
        var screenCol = new Color(0.42f, 0.53f, 0.62f, 0.85f).Lerp(new Color(0.72f, 0.80f, 0.84f, 0.85f), Clear * 0.4f);
        DrawRect(new Rect2(mx, my, mw, mh), screenCol);
        // 屏上的"论文终稿"：几行细线
        for (int i = 0; i < 5; i++)
        {
            float lw = mw * (0.55f + 0.08f * (i % 3));
            DrawRect(new Rect2(mx + mw * 0.08f, my + mh * (0.16f + i * 0.14f), lw, 2f),
                new Color(0.20f, 0.26f, 0.32f, 0.55f));
        }

        // ---- 台灯（右侧，暖黄一圈光）----
        float lx = w * 0.76f, ly = h * 0.74f;
        var warm = new Color(1f, 0.86f, 0.62f, 0.10f);
        DrawCircle(new Vector2(lx, ly), h * 0.16f, warm);
        DrawCircle(new Vector2(lx, ly), h * 0.09f, new Color(1f, 0.88f, 0.66f, 0.16f));
        DrawCircle(new Vector2(lx, ly), h * 0.02f, new Color(1f, 0.92f, 0.72f, 0.75f));

        // ---- 窗台上她的发圈（米白小点；flavor 热点的视觉锚）----
        DrawCircle(new Vector2(w * 0.185f, skyH - 6f), 5.5f, new Color(0.90f, 0.87f, 0.78f, 0.9f));
        DrawCircle(new Vector2(w * 0.185f, skyH - 6f), 2.2f, new Color(0.62f, 0.55f, 0.42f, 0.7f));

        // ---- 窗台上的手机（竖着靠在窗框，屏还亮着——她三条消息）----
        float pw = w * 0.040f, ph = h * 0.088f;
        float px = w * 0.226f, py = skyH - ph - 4f;
        DrawCircle(new Vector2(px + pw * 0.5f, py + ph * 0.5f), w * 0.055f, new Color(0.50f, 0.66f, 0.86f, 0.055f));
        DrawRect(new Rect2(px, py, pw, ph), new Color(0.055f, 0.065f, 0.085f));
        DrawRect(new Rect2(px + 2.5f, py + 4f, pw - 5f, ph - 8f),
            new Color(0.52f, 0.68f, 0.82f, 0.78f + Clear * 0.12f));
        // 屏幕上两行"未读"的小横线
        DrawRect(new Rect2(px + pw * 0.16f, py + ph * 0.22f, pw * 0.68f, 2f), new Color(0.20f, 0.28f, 0.36f, 0.55f));
        DrawRect(new Rect2(px + pw * 0.16f, py + ph * 0.38f, pw * 0.52f, 2f), new Color(0.20f, 0.28f, 0.36f, 0.45f));

        // ---- Dim 压暗 ----
        if (Dim > 0.001f)
            DrawRect(new Rect2(0, 0, w, h), new Color(0.03f, 0.035f, 0.05f, Dim * 0.62f));
    }

    /// <summary>窗外的树：主干 + 三团叶子的梧桐剪影；sway 是摆幅（0~1）</summary>
    private void DrawTree(float baseX, float baseY, float treeH, float sway)
    {
        var col = new Color(0.10f, 0.13f, 0.16f, 0.85f);
        float bend = Mathf.Sin(t * 1.7f) * sway * 10f + Mathf.Sin(t * 3.9f) * sway * 4f;
        var trunkTop = new Vector2(baseX + bend * 0.4f, baseY - treeH * 0.55f);
        DrawLine(new Vector2(baseX, baseY), trunkTop, col, 7f);
        // 三团叶子（位置随 bend 摆）
        DrawCircle(trunkTop + new Vector2(bend, -treeH * 0.10f), treeH * 0.20f, col);
        DrawCircle(trunkTop + new Vector2(bend - treeH * 0.14f, -treeH * 0.02f), treeH * 0.15f, col);
        DrawCircle(trunkTop + new Vector2(bend + treeH * 0.13f, -treeH * 0.04f), treeH * 0.16f, col);
        // 两根枝
        DrawLine(trunkTop, trunkTop + new Vector2(bend * 1.4f - treeH * 0.14f, -treeH * 0.16f), col, 3f);
        DrawLine(trunkTop, trunkTop + new Vector2(bend * 1.4f + treeH * 0.12f, -treeH * 0.14f), col, 3f);
    }
}
