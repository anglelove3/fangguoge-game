using Godot;
using System.Collections.Generic;

/// <summary>
/// 第三章：天平
///
/// 流程：咖啡馆的转折（抽象化）→ 三张记忆卡片放上左盘
///      → 天平失衡的揭示 → 分手台词（选择）→ 尾声 → 第四章
///
/// 【本章演出核心】全程序化绘制的天平（BalanceScale），
/// 卡片点击后自己"飞"进左盘、变成纸片；横梁随重量倾斜，
/// 最后揭晓：右边那颗很小的光点，比左边所有记忆都重。
/// </summary>
public partial class Ch03 : ChapterBase
{
    private const string ChapterId = "ch03"; // 对应 data/dialogues/ch03.json

    /// <summary>三张记忆卡片：id / 标题 / 副题 / 屏幕位置 / 飞入时的纸片角</summary>
    private static readonly CardDef[] Cards =
    {
        new("ticket", "铁盒里的车票", "一千公里，来来回回", new Vector2(170, 280), -0.032f),
        new("notes", "高三的错题本", "你抄的，她划的重点", new Vector2(120, 660), 0.026f),
        new("scarf", "织到一半的围巾", "赶在她的生日之前……", new Vector2(1420, 300), -0.024f),
    };

    private sealed record CardDef(string Id, string Title, string Subtitle, Vector2 Pos, float Tilt);

    private BalanceScale scale;
    private Control cardLayer;
    private Button rightPanBtn;
    private Control rightPanGlow;
    private Control hotspotsRoot;
    private Label hintLabel;

    private readonly HashSet<string> doneCards = new();
    private readonly Dictionary<string, Button> cardButtons = new();
    private readonly Dictionary<string, Control> cardGlows = new();

    private int rightPanStage; // 右盘彩蛋：第几次点
    private bool revealed;     // 天平真相已揭晓
    private bool finished;     // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第三章：天平 —— 有些重量，天平称不出来");

        scale = GetNode<BalanceScale>("BalanceScale");
        cardLayer = GetNode<Control>("CardLayer");
        rightPanBtn = GetNode<Button>("Hotspots/RightPanBtn");
        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        rightPanBtn.Pressed += OnRightPanPressed;
        rightPanGlow = HotspotGlow.Attach(rightPanBtn);

        foreach (var def in Cards)
            cardButtons[def.Id] = BuildCard(def);

        UiSounds.WireAll(this);

        // 开场对白放完之前，一切都不能点
        rightPanBtn.Disabled = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 章节标题缓缓淡出
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 还没放的卡片轻轻呼吸</summary>
    public override void _Process(double delta)
    {
        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsBusy)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (!hotspotsRoot.Visible || revealed)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();

        float delay = 0f;
        foreach (var def in Cards)
        {
            if (doneCards.Contains(def.Id))
                continue;
            if (!cardButtons.TryGetValue(def.Id, out var btn) || !IsInstanceValid(btn))
                continue;
            if (cardGlows.TryGetValue(def.Id, out var glow) && glow != null)
            {
                HotspotGlow.Pulse(glow, delay);
                delay += 0.4f;
            }
        }
    }

    // ==================== 开场结束：卡片浮现 ====================

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        rightPanBtn.Disabled = false;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 三张卡片依次浮现（像从记忆里慢慢显影）
        float delay = 0.2f;
        foreach (var def in Cards)
        {
            if (!cardButtons.TryGetValue(def.Id, out var btn))
                continue;
            var tw = CreateTween();
            tw.TweenInterval(delay);
            tw.TweenCallback(Callable.From(() => btn.Disabled = false));
            tw.SetParallel(true);
            tw.TweenProperty(btn, "modulate:a", 1f, 0.5);
            tw.TweenProperty(btn, "scale", Vector2.One, 0.5)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            delay += 0.18f;
        }
    }

    private void UpdateHint()
    {
        if (revealed)
        {
            hintLabel.Visible = false;
            return;
        }
        int n = doneCards.Count;
        hintLabel.Text = n >= 3
            ? "（左边已经放满了……可天平还没给出答案）"
            : $"点开三张记忆卡片，把它们放上天平（{n}/3）";
    }

    // ==================== 记忆卡片 ====================

    /// <summary>做一张"奶油纸片"记忆卡片（按钮 + 标题 + 副题）</summary>
    private Button BuildCard(CardDef def)
    {
        var btn = new Button
        {
            Position = def.Pos,
            Size = new Vector2(340, 168),
            PivotOffset = new Vector2(170, 84),
            Scale = new Vector2(0.9f, 0.9f),
            Modulate = new Color(1, 1, 1, 0),
            Disabled = true,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeStyleboxOverride("normal", MakePaper(new Color(0.93f, 0.88f, 0.78f), 10));
        btn.AddThemeStyleboxOverride("hover", MakePaper(new Color(0.97f, 0.93f, 0.84f), 18));
        btn.AddThemeStyleboxOverride("pressed", MakePaper(new Color(0.86f, 0.80f, 0.68f), 6));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 12);

        var title = new Label
        {
            Text = def.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(0.30f, 0.23f, 0.15f));

        var sub = new Label
        {
            Text = def.Subtitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        sub.AddThemeFontSizeOverride("font_size", 20);
        sub.AddThemeColorOverride("font_color", new Color(0.47f, 0.39f, 0.28f));

        box.AddChild(title);
        box.AddChild(sub);
        margin.AddChild(box);
        btn.AddChild(margin);

        // 悬停时轻轻抬起
        btn.MouseEntered += () => TweenCardScale(btn, 1.04f, 0.14f);
        btn.MouseExited += () => TweenCardScale(btn, 1.0f, 0.16f);

        btn.Pressed += () => OnCardPressed(def);
        cardLayer.AddChild(btn);
        cardGlows[def.Id] = HotspotGlow.Attach(btn);
        return btn;
    }

    private static StyleBoxFlat MakePaper(Color color, int shadow)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll(16);
        sb.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        sb.ShadowSize = shadow;
        sb.ShadowOffset = new Vector2(0, shadow * 0.5f);
        return sb;
    }

    private static void TweenCardScale(Button btn, float s, float d)
    {
        if (btn.HasMeta("flying"))
            return;
        var tw = btn.CreateTween();
        tw.TweenProperty(btn, "scale", new Vector2(s, s), d)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void OnCardPressed(CardDef def)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || revealed || doneCards.Contains(def.Id))
            return;
        if (!cardButtons.TryGetValue(def.Id, out var btn) || !IsInstanceValid(btn))
            return;

        lastActivityMsec = Time.GetTicksMsec();
        doneCards.Add(def.Id);
        HotspotGlow.Sparkle(btn);
        UpdateHint();

        dm.PlaySequence(ChapterId, $"card_{def.Id}", () => FlyCardToPan(def, btn));
    }

    /// <summary>回忆放完 → 卡片飞进左盘、缩成一张纸片码上去，天平倾斜</summary>
    private void FlyCardToPan(CardDef def, Button btn)
    {
        btn.Disabled = true;
        btn.SetMeta("flying", true);

        Vector2 localTarget = cardLayer.GetGlobalTransform().AffineInverse() * scale.LeftPanDropGlobal();
        Vector2 posTarget = localTarget - btn.Size * 0.5f;

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(btn, "position", posTarget, 0.85)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(btn, "scale", new Vector2(0.3f, 0.3f), 0.85).SetEase(Tween.EaseType.In);
        tw.TweenProperty(btn, "rotation", def.Tilt, 0.85);
        tw.TweenProperty(btn, "modulate:a", 0.0f, 0.6).SetDelay(0.3);
        tw.Chain().TweenCallback(Callable.From(() =>
        {
            btn.QueueFree();
            cardButtons.Remove(def.Id);
            scale.AddMemoryChip();
            int n = doneCards.Count;
            scale.SetTilt(n switch { 1 => -7f, 2 => -14f, _ => -20f });
            lastActivityMsec = Time.GetTicksMsec();
            if (n >= 3)
                GetTree().CreateTimer(0.9).Timeout += PlayReveal;
        }));
    }

    // ==================== 右盘彩蛋 ====================

    private void OnRightPanPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || revealed)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(rightPanBtn);
        rightPanStage++;
        string seq = rightPanStage switch
        {
            1 => "right_pan_1",
            2 => "right_pan_2",
            _ => "right_pan_more",
        };
        dm.PlaySequence(ChapterId, seq, null);
    }

    // ==================== 揭晓 → 选择 → 尾声 ====================

    private void PlayReveal()
    {
        if (revealed || finished)
            return;

        revealed = true;
        rightPanBtn.Disabled = true;
        UpdateHint();

        scale.SetTilt(10f); // 天平终于动了——朝右边
        DialogueManager.Instance.PlaySequence(ChapterId, "all_cards", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "breakup", OnBreakupChosen);
        });
    }

    private void OnBreakupChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"say_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                finished = true;
                GoToNextChapter("res://scenes/chapters/ch04/ch04.tscn", 4);
            });
        });
    }
}
