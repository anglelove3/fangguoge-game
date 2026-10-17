using Godot;

/// <summary>
/// 海边打水漂（第三轮：碧蓝蓝天章的轻玩法）
///
/// 按住鼠标蓄力，松手扔出去。力度合适石子才会在水面上弹——
/// 太轻：直接沉；正好：弹五下六下；太重：砸进水里溅一脸。
///
/// 弹完的圈数带回来一句他的反应。没有分数、没有失败，
/// 就像那个下午：什么也没发生，但好受了一点。
/// </summary>
public partial class SkippingStone : Control
{
    private readonly System.Action<int> onDone;
    private float t;
    private float charge;        // 蓄力 0~1
    private bool charging;
    private bool thrown;
    private float throwT;
    private int bounces;
    private bool settled;
    private bool finished;

    private Vector2 startPos;
    private readonly System.Collections.Generic.List<(Vector2 pos, float r, float a)> ripples = new();

    private const float MaxCharge = 1.4f;

    public SkippingStone(System.Action<int> done)
    {
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var size = GetViewportRect().Size;
        startPos = new Vector2(size.X * 0.46f, size.Y * 0.74f);
    }

    public override void _Input(InputEvent @event)
    {
        if (finished)
            return;
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && !thrown)
                charging = true;
            else if (!mb.Pressed && charging && !thrown)
            {
                charging = false;
                thrown = true;
                throwT = 0f;
                // 力度决定弹跳次数：0.45~0.8 甜区（弹 4~6 下），太轻/太重都少
                float sweetness = 1f - Mathf.Abs(charge - 0.62f) / 0.62f;
                bounces = Mathf.Clamp(Mathf.RoundToInt(sweetness * 6.5f), 0, 6);
                AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -18f, 0.04f, 1.4f);
            }
        }
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (charging)
            charge = Mathf.Min(charge + (float)delta / MaxCharge, 1f);

        if (thrown)
        {
            throwT += (float)delta;
            // 石子在水面弹跳：每 0.28 秒落一次，涟漪一圈圈扩
            float interval = 0.28f;
            int shouldHave = Mathf.Min((int)(throwT / interval), bounces);
            while (ripples.Count < shouldHave)
            {
                float x = startPos.X + 60f + (ripples.Count + 1) * 44f;
                float y = startPos.Y + Mathf.Sin((ripples.Count + 1) * 1.7f) * 6f;
                ripples.Add((new Vector2(x, y), 4f, 0.85f));
                AudioManager.Instance?.PlaySfx(AudioManager.SfxTick, -22f, 0.02f, 1.9f);
            }
            for (int i = 0; i < ripples.Count; i++)
            {
                var (p, r, a) = ripples[i];
                ripples[i] = (p, r + (float)delta * 26f, a - (float)delta * 0.32f);
            }

            if (!settled && throwT > bounces * interval + 1.2f)
            {
                settled = true;
                // 静一拍，让他看着水面
                var timer = GetTree().CreateTimer(1.0f);
                timer.Timeout += () =>
                {
                    finished = true;
                    var cb = onDone;
                    QueueFree();
                    cb?.Invoke(bounces);
                };
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        // 蓄力提示：水面上方一条很短的力度线（不用 UI 框，画在画面里）
        if (!thrown)
        {
            var font = GetThemeDefaultFont();
            DrawString(font, new Vector2(size.X * 0.42f, size.Y * 0.82f),
                "（按住蓄力，松手扔出去。）", HorizontalAlignment.Left, -1, 22,
                new Color(0.95f, 0.95f, 0.92f, 0.6f + 0.2f * Mathf.Sin(t * 3f)));

            float w = 260f * charge;
            var barPos = new Vector2(size.X * 0.5f - 130f, size.Y * 0.85f);
            DrawRect(new Rect2(barPos, new Vector2(260, 6)), new Color(1f, 1f, 1f, 0.12f));
            DrawRect(new Rect2(barPos, new Vector2(w, 6)),
                charge > 0.45f && charge < 0.8f ? new Color(1f, 0.92f, 0.64f, 0.85f)
                                                  : new Color(0.85f, 0.88f, 0.92f, 0.6f));
            // 甜区标记
            DrawRect(new Rect2(barPos + new Vector2(260 * 0.45f, -3), new Vector2(260 * 0.35f, 12)),
                new Color(1f, 0.95f, 0.7f, 0.10f));
        }

        // 石子（一个小黑点，飞出去）
        if (thrown)
        {
            float x = startPos.X + Mathf.Min(throwT * 600f, size.X * 0.42f);
            float y = startPos.Y - Mathf.Abs(Mathf.Sin(throwT * 9f)) * 12f * (throwT < bounces * 0.28f ? 1f : 0f);
            DrawCircle(new Vector2(x, y), 3.5f, new Color(0.18f, 0.18f, 0.20f, 0.9f));
        }

        // 涟漪
        foreach (var (p, r, a) in ripples)
        {
            if (a <= 0f)
                continue;
            DrawArc(p, r, 0, Mathf.Tau, 28, new Color(1f, 1f, 1f, a * 0.45f), 1.6f);
            DrawArc(p, r * 0.6f, 0, Mathf.Tau, 24, new Color(1f, 1f, 1f, a * 0.25f), 1.2f);
        }
    }
}
