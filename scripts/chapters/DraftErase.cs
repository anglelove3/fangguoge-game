using Godot;

/// <summary>
/// 第六章《晚安》——「打了又删」演出（第三轮 A1）
///
/// 选了「删了」之后：画面中央浮出一个输入框，框里是那句没发出去的话。
/// 光标停在末尾——然后一个字、一个字地消失。退格声轻得像叹息。
/// 删完了，空框再多停一会儿。有些决定不是在按下发送时做的，
/// 是在看着最后一个字消失的时候做的。
///
/// 纯代码绘制，播完自毁。
/// </summary>
public partial class DraftErase : Control
{
    private readonly string fullText;
    private readonly System.Action onDone;
    private float t;

    private const float BoxIn = 0.7f;      // 输入框浮现
    private const float Hold = 1.1f;       // 整句话先停一会儿（像在读最后一遍）
    private const float ErasePerChar = 0.16f;
    private const float EmptyHold = 1.3f;  // 空框多停一会儿
    private const float FadeOut = 0.6f;

    private float total;
    private Font font;

    public DraftErase(string text, System.Action done)
    {
        fullText = text;
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        total = BoxIn + Hold + ErasePerChar * fullText.Length + EmptyHold + FadeOut;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t >= total)
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        font ??= GetThemeDefaultFont();
        var size = GetViewportRect().Size;
        float cx = size.X / 2f;
        float cy = size.Y * 0.62f;   // 屏幕中偏下——输入框的位置感

        float fadeOutP = Mathf.Clamp((t - (total - FadeOut)) / FadeOut, 0f, 1f);
        float master = 1f - fadeOutP;

        // 背景微压暗（把注意力收到框上）
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.03f, 0.03f, 0.06f, 0.42f * master));

        // ---- 删到还剩几个字 ----
        float eraseT = t - BoxIn - Hold;
        int left = eraseT <= 0f ? fullText.Length
            : Mathf.Max(0, fullText.Length - (int)(eraseT / ErasePerChar));
        string shown = fullText[..left];

        // 输入框
        float boxW = Mathf.Min(size.X * 0.52f, 560f);
        var rect = new Rect2(cx - boxW / 2f, cy - 40f, boxW, 84f);
        float boxInP = Mathf.Clamp(t / BoxIn, 0f, 1f);
        var boxCol = new Color(0.94f, 0.93f, 0.90f, 0.92f * boxInP * master);
        DrawRect(rect, boxCol);
        DrawRect(rect, new Color(0.5f, 0.48f, 0.44f, 0.6f * boxInP * master), false, 2f);

        // 框内文字（从右往左消失）
        if (boxInP > 0.6f)
        {
            var textCol = new Color(0.12f, 0.11f, 0.10f, master);
            DrawString(font, new Vector2(rect.Position.X + 20f, rect.Position.Y + 54f),
                shown, HorizontalAlignment.Left, boxW - 40f, 28, textCol);

            // 光标（闪烁，停在末尾）
            float blink = Mathf.Sin(t * 7f) > 0 ? 1f : 0.15f;
            float textW = shown.Length > 0
                ? font.GetStringSize(shown, HorizontalAlignment.Left, -1, 28).X
                : 0f;
            float cx2 = rect.Position.X + 20f + textW + 2f;
            DrawLine(new Vector2(cx2, rect.Position.Y + 16f), new Vector2(cx2, rect.Position.Y + 62f),
                new Color(0.12f, 0.11f, 0.10f, blink * master), 2.2f);
        }

        // 退格声：每消失一个字轻轻「哒」一声（很轻的机械音）
        int erasedCount = fullText.Length - left;
        if (eraseT > 0f && erasedCount != lastErased)
        {
            lastErased = erasedCount;
            AudioManager.Instance?.PlaySfx(AudioManager.SfxTick, -18f, 0.02f, 1.35f);
        }
    }

    private int lastErased;
}
