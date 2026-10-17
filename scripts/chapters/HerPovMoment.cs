using Godot;

/// <summary>
/// 她视角（第三轮：《星月》章插入的 13 秒）
///
/// 他走过去之前，有一段他不知道的事——
/// 她低头看手机的那一下，她的手机在兜里震过。
///
/// 屏幕亮起，一条消息：「今天没去上班？」
/// 她看了一眼。没回。把手机按灭了。
/// 然后是她的声音，很轻：「（……你先过去吧。）」
///
/// 不发一句解释。谁发的、她为什么没回——玩家自己懂。
/// </summary>
public partial class HerPovMoment : Control
{
    private readonly System.Action onDone;
    private float t;

    private ColorRect screenGlow;     // 屏幕光（照亮她脸的那点光）
    private Panel screen;             // 手机屏幕
    private Label msgSpeaker;
    private Panel msgBubble;
    private Label msgText;
    private Label caption;

    private string[] captions;
    private float[] capTimes;

    public HerPovMoment(System.Action done)
    {
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var size = GetViewportRect().Size;

        // 全黑（但带一点冷的蓝——夜里广场的光）
        var bg = new ColorRect { Color = new Color(0.035f, 0.04f, 0.055f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 屏幕光（一圈很淡的冷光，从手机位置散开）
        screenGlow = new ColorRect
        {
            Color = new Color(0.55f, 0.68f, 0.85f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var gs = size * 0.6f;
        screenGlow.Position = new Vector2(size.X * 0.5f - gs.X / 2f, size.Y * 0.5f - gs.Y / 2f);
        screenGlow.Size = gs;
        AddChild(screenGlow);

        // 手机屏幕（竖着的圆角矩形）
        screen = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        var sStyle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.09f) };
        sStyle.SetCornerRadiusAll(18);
        screen.AddThemeStyleboxOverride("panel", sStyle);
        var sw = size.X * 0.28f;
        var sh = sw * 1.72f;
        screen.Size = new Vector2(sw, sh);
        screen.Position = new Vector2((size.X - sw) / 2f, (size.Y - sh) / 2f);
        AddChild(screen);

        // 消息发送者
        msgSpeaker = new Label { Text = "", MouseFilter = MouseFilterEnum.Ignore };
        msgSpeaker.AddThemeFontSizeOverride("font_size", 20);
        msgSpeaker.AddThemeColorOverride("font_color", new Color(0.62f, 0.66f, 0.72f));
        msgSpeaker.Position = new Vector2(28, sh * 0.22f);
        screen.AddChild(msgSpeaker);

        // 消息气泡（对方发来的，左边）
        msgBubble = new Panel { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        var bStyle = new StyleBoxFlat { BgColor = new Color(0.92f, 0.93f, 0.95f) };
        bStyle.SetCornerRadiusAll(12);
        bStyle.ContentMarginLeft = 16; bStyle.ContentMarginRight = 16;
        bStyle.ContentMarginTop = 10; bStyle.ContentMarginBottom = 10;
        msgBubble.AddThemeStyleboxOverride("panel", bStyle);
        msgText = new Label { Text = "今天没去上班？", MouseFilter = MouseFilterEnum.Ignore };
        msgText.AddThemeFontSizeOverride("font_size", 22);
        msgText.AddThemeColorOverride("font_color", new Color(0.14f, 0.14f, 0.16f));
        msgBubble.AddChild(msgText);
        screen.AddChild(msgBubble);

        // 字幕（画外音，在屏幕下方）
        caption = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        caption.AddThemeFontSizeOverride("font_size", 24);
        caption.AddThemeColorOverride("font_color", new Color(0.82f, 0.84f, 0.88f, 0f));
        caption.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.5f));
        caption.AddThemeConstantOverride("outline_size", 5);
        caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        caption.OffsetBottom = -size.Y * 0.12f;
        AddChild(caption);

        captions = new[]
        {
            "（同一时间。）",
            "",
            "（她的手机在兜里震了一下。）",
            "（她低头看了一眼。）",
            "（没回。）",
            "（把手机按灭了。）",
            "（远处有人走过来。她抬起头。）",
        };
        capTimes = new[] { 0.3f, 1.6f, 3.4f, 6.0f, 8.4f, 10.2f, 12.0f };

        AudioManager.Instance?.PlayBgm("", 0f, 1.6f);
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float sizeOk = GetViewportRect().Size.X;

        // 字幕推进
        for (int i = 0; i < captions.Length; i++)
        {
            if (string.IsNullOrEmpty(captions[i]))
                continue;
            if (t >= capTimes[i] && t < capTimes[i] + 2.0f)
            {
                float a = Mathf.Clamp((t - capTimes[i]) / 0.4f, 0f, 1f)
                        * Mathf.Clamp((capTimes[i] + 2.0f - t) / 0.4f, 0f, 1f);
                if (caption.Text != captions[i])
                    caption.Text = captions[i];
                caption.AddThemeColorOverride("font_color", new Color(0.82f, 0.84f, 0.88f, a));
            }
        }

        // 屏幕亮起 → 消息出现 → 熄灭
        if (screen != null)
        {
            // 屏幕光（2.6s 亮起，10.5s 熄灭）
            float glowA = 0f;
            if (t > 2.6f && t < 10.6f)
                glowA = Mathf.Clamp((t - 2.6f) / 0.5f, 0f, 1f) * Mathf.Clamp((10.6f - t) / 0.5f, 0f, 1f);
            screenGlow.Color = new Color(0.55f, 0.68f, 0.85f, glowA * 0.10f);

            // 屏幕内亮（消息出现后）
            float screenBright = glowA;

            // 气泡：3.8s 出现
            if (t > 3.8f && t < 10.2f)
            {
                float ba = Mathf.Clamp((t - 3.8f) / 0.4f, 0f, 1f);
                msgBubble.Modulate = new Color(1, 1, 1, ba);
                msgSpeaker.Text = "";
                var bubbleW = msgText.GetMinimumSize().X + 32;
                msgBubble.Size = new Vector2(bubbleW, msgText.GetMinimumSize().Y + 20);
                msgBubble.Position = new Vector2(24, screen.Size.Y * 0.3f);
                msgText.Position = Vector2.Zero;
            }
            else
            {
                msgBubble.Modulate = new Color(1, 1, 1, 0);
                msgSpeaker.Text = t > 3.4f && t < 10.4f ? "他" : "";
            }

            // 屏幕整体亮度（熄灭时压暗）
            screen.Modulate = new Color(0.55f + 0.45f * screenBright, 0.58f + 0.42f * screenBright,
                                        0.62f + 0.38f * screenBright);
        }

        if (t >= 13.6f)
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke();
            return;
        }
    }
}
