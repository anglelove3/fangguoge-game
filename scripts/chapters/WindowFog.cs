using Godot;

/// <summary>
/// 第二章《一千公里》——「在窗玻璃上，把两座城市连成线」演出（第二十四轮 F32）
///
/// 全屏一层：整块玻璃蒙上哈气 → 指尖从「他」这一端划出一条弧线，
/// 一头浮出「安徽·马鞍山」，另一头浮出「四川·成都」，一线牵两城；
/// 最后一颗光点沿着线从这头走到那头——一千公里，也就一拃的长度。
///
/// 纯代码绘制（雾面用多层半透明圆斑叠加，线条用贝塞尔采样），零新素材。
/// 播完自毁；对白与演出并行，不阻塞叙事。
/// </summary>
public partial class WindowFog : Control
{
    private float t;
    private bool finished;

    // 时序（秒）
    private const float FogIn = 1.2f;      // 哈气
    private const float Pause = 0.4f;
    private const float DrawLine = 1.6f;   // 划线
    private const float NamesIn = 0.8f;    // 地名浮现
    private const float StarWalk = 1.4f;   // 光点沿线
    private const float HoldEnd = 1.2f;
    private const float FadeOut = 0.9f;
    private static float Total => FogIn + Pause + DrawLine + NamesIn + StarWalk + HoldEnd + FadeOut;

    // 线的起止（屏幕比例坐标：起=他的城市侧，终=她的城市侧）
    private static readonly Vector2 From = new(0.26f, 0.66f);
    private static readonly Vector2 To = new(0.74f, 0.38f);

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t >= Total && !finished)
        {
            finished = true;
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var a = new Vector2(From.X * size.X, From.Y * size.Y);
        var b = new Vector2(To.X * size.X, To.Y * size.Y);
        // 控制点：让线带一点「手指划过」的自然弧度
        var ctrl = new Vector2((a.X + b.X) * 0.5f, Mathf.Min(a.Y, b.Y) - size.Y * 0.10f);

        float local(float segStart, float segDur) => Mathf.Clamp((t - segStart) / segDur, 0f, 1f);

        // ---- 1. 哈气雾面（多层圆斑，从中心往外洇开） ----
        float fog = local(0f, FogIn);
        if (fog > 0f)
        {
            var rng = new RandomNumberGenerator();
            rng.Seed = 20261015; // 固定种子：雾斑位置稳定不闪
            for (int i = 0; i < 26; i++)
            {
                float fx = (0.12f + rng.Randf() * 0.76f) * size.X;
                float fy = (0.18f + rng.Randf() * 0.60f) * size.Y;
                float fr = (40f + rng.Randf() * 90f) * size.Y / 1080f;
                float fa = fog * 0.10f * (0.6f + rng.Randf() * 0.4f);
                DrawCircle(new Vector2(fx, fy), fr, new Color(0.92f, 0.95f, 1f, fa));
            }
            // 整体薄雾罩
            DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.90f, 0.94f, 1f, fog * 0.10f));
        }

        // ---- 2. 指尖划线（哈气完成后开始） ----
        float lineP = local(FogIn + Pause, DrawLine);
        if (lineP > 0f)
        {
            // 划过的部分：亮线 + 雾被指尖抹开的透明痕
            var pts = new System.Collections.Generic.List<Vector2>();
            int steps = 42;
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                if (u > lineP) break;
                pts.Add(Bezier(a, ctrl, b, u));
            }
            if (pts.Count >= 2)
            {
                // 抹开的透明痕（宽、半透黑——像雾被擦掉）
                DrawPolyline(pts.ToArray(), new Color(0.05f, 0.05f, 0.10f, 0.28f), 22f);
                // 线本体（暖白，两段：粗芯 + 亮边）
                DrawPolyline(pts.ToArray(), new Color(1f, 0.95f, 0.82f, 0.5f), 6f);
                DrawPolyline(pts.ToArray(), new Color(1f, 0.99f, 0.92f, 0.9f), 2.4f);

                // 指尖光点（划线进行中的移动亮点）
                if (lineP < 1f)
                    DrawCircle(pts[^1], 7f, new Color(1f, 0.98f, 0.9f, 0.95f));
            }
        }

        // ---- 3. 两端地名浮现 ----
        float nameP = local(FogIn + Pause + DrawLine, NamesIn);
        if (nameP > 0f)
        {
            DrawName(size, a, "安徽·马鞍山", nameP, below: true);
            DrawName(size, b, "四川·成都", nameP, below: false);
        }

        // ---- 4. 光点沿线从这头走到那头 ----
        float starT0 = FogIn + Pause + DrawLine + NamesIn;
        float starP = local(starT0, StarWalk);
        if (starP > 0f && starP < 1f)
        {
            var pos = Bezier(a, ctrl, b, starP);
            float tw = 0.75f + 0.25f * Mathf.Sin(t * 9f);
            DrawCircle(pos, 6f, new Color(1f, 0.97f, 0.86f, 0.95f * tw));
            DrawArc(pos, 13f, 0f, Mathf.Tau, 24, new Color(1f, 0.95f, 0.8f, 0.5f * tw), 2f);
        }

        // ---- 5. 收尾淡出 ----
        float fadeStart = Total - FadeOut;
        if (t > fadeStart)
            DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0, 0, 0, (t - fadeStart) / FadeOut));
    }

    private Font drawFont;
    private void DrawName(Vector2 size, Vector2 anchor, string text, float p, bool below)
    {
        drawFont ??= GetThemeDefaultFont();
        float fa = p;
        var sz = drawFont.GetStringSize(text, HorizontalAlignment.Left, -1, 30);
        var pos = new Vector2(
            Mathf.Clamp(anchor.X - sz.X * 0.5f, 24f, size.X - sz.X - 24f),
            anchor.Y + (below ? 46f : -sz.Y - 40f));
        DrawString(drawFont, pos + new Vector2(2, 2), text, HorizontalAlignment.Left, -1, 30, new Color(0, 0, 0, 0.4f * fa));
        DrawString(drawFont, pos, text, HorizontalAlignment.Left, -1, 30, new Color(1f, 0.97f, 0.9f, 0.95f * fa));
        // 地名下的小横线（像手写落款）
        DrawLine(new Vector2(pos.X, pos.Y + sz.Y + 8), new Vector2(pos.X + sz.X * p, pos.Y + sz.Y + 8),
            new Color(1f, 0.95f, 0.82f, 0.7f * fa), 2f);
    }

    private static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t) =>
        (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
}
