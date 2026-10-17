using Godot;
using System.Collections.Generic;

/// <summary>
/// 窗上雨（第三轮：《三点水》章的雨夜感）
///
/// 全屏一层薄薄的雨：斜着的细雨线，窗玻璃上一颗一颗的水珠，
/// 偶尔一颗慢慢滑下来，拖出一道水痕。
/// 不是特效大片，是「屋里的人看着窗外下了一夜雨」的那种雨。
/// </summary>
public partial class RainWindow : Control
{
    private float t;
    private class Drop
    {
        public Vector2 Pos;
        public float R;
        public float Speed;     // 0 = 挂着不动
        public float SlideIn;   // 还要多久开始滑
        public bool Sliding;
        public float Trail;
    }

    private readonly List<Drop> drops = new();
    private readonly System.Random rng = new(7);
    private Vector2 size;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        size = GetViewportRect().Size;
        for (int i = 0; i < 46; i++)
        {
            drops.Add(new Drop
            {
                Pos = new Vector2((float)rng.NextDouble() * size.X, (float)rng.NextDouble() * size.Y),
                R = 1.5f + (float)rng.NextDouble() * 3.2f,
                Speed = 0f,
                SlideIn = 2f + (float)rng.NextDouble() * 14f,
            });
        }
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        foreach (var d in drops)
        {
            if (!d.Sliding)
            {
                d.SlideIn -= (float)delta;
                if (d.SlideIn <= 0f)
                {
                    d.Sliding = true;
                    d.Speed = 14f + (float)rng.NextDouble() * 26f;
                }
            }
            else
            {
                d.Speed += 30f * (float)delta;
                d.Pos.Y += d.Speed * (float)delta;
                d.Trail = Mathf.Min(d.Trail + (float)delta * 90f, 60f);
                if (d.Pos.Y > size.Y + 10f)
                {
                    // 落到窗台以下：换个位置重新挂
                    d.Pos = new Vector2((float)rng.NextDouble() * size.X, -12f);
                    d.Speed = 0f;
                    d.Sliding = false;
                    d.SlideIn = 2f + (float)rng.NextDouble() * 12f;
                    d.Trail = 0f;
                }
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 斜雨线（整体很淡，只在画面上半部密一点）
        var lineCol = new Color(0.78f, 0.84f, 0.90f, 0.10f);
        for (int i = 0; i < 70; i++)
        {
            float seed = i * 37.7f;
            float x = (seed * 3.1f + t * 90f) % (size.X + 200f) - 100f;
            float y = (seed * 7.3f + t * 260f) % (size.Y + 120f) - 60f;
            DrawLine(new Vector2(x, y), new Vector2(x - 12f, y + 26f), lineCol, 1.1f);
        }

        // 窗玻璃上的水珠
        foreach (var d in drops)
        {
            if (d.Sliding && d.Trail > 1f)
            {
                // 拖痕
                DrawLine(new Vector2(d.Pos.X, d.Pos.Y - d.Trail), d.Pos,
                    new Color(0.85f, 0.90f, 0.95f, 0.10f), d.R * 0.9f);
            }
            DrawCircle(d.Pos, d.R, new Color(0.88f, 0.92f, 0.96f, 0.16f));
            DrawCircle(d.Pos + new Vector2(-d.R * 0.3f, -d.R * 0.3f), d.R * 0.35f,
                new Color(1f, 1f, 1f, 0.22f));
        }
    }
}
