using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第三章：天平
///
/// 流程：咖啡馆的转折（抽象化）→ 三张记忆卡片放上左盘
///      → 【天平的失败互动】把"还能给的"一样一样补上去，它反而越歪越厉害
///      → 天平失衡的揭示 → 分手台词（选择）→ 尾声 → 第四章
///
/// 【本章演出核心】全程序化绘制的天平（BalanceScale），
/// 卡片点击后自己"飞"进左盘、变成纸片；横梁随重量倾斜，
/// 最后揭晓：右边那颗很小的光点，比左边所有记忆都重。
///
/// 【第十一轮改了什么】
///   A3：卡片/补偿物的坐标、天平的位置全部搬进 data/chapters/ch03.json，
///       天平整体上移，不再被对话框压住（原来右盘 100% 看不见）。
///   C4：三张卡片换上水彩配图（assets/art/cards/），不再只是两张字条。
///   C7：加了设计方案里承诺过的"想让天平平衡，但做不到"——
///       三张卡片放完后会浮出三样"补偿物"（道歉/成绩/熬夜），
///       每往左盘加一样，横梁就歪得更狠；这才是这一章想让玩家体会的事。
/// </summary>
public partial class Ch03 : ChapterBase
{
    private const string ChapterId = "ch03"; // 对应 data/dialogues/ch03.json

    // 本章的可调数据（卡片/补偿物的文案键、坐标、倾角）
    private ChapterLayout layout = new();

    private BalanceScale scale;
    private Control cardLayer;
    private Button rightPanBtn;
    private Control rightPanGlow;
    private Control hotspotsRoot;
    private Label hintLabel;

    private readonly HashSet<string> doneCards = new();
    private readonly HashSet<string> doneChips = new();
    private readonly Dictionary<string, Button> cardButtons = new();
    private readonly Dictionary<string, Button> chipButtons = new();
    private readonly Dictionary<string, Control> glows = new();

    private int rightPanStage; // 右盘彩蛋：第几次点
    private bool orbAwarded;   // 隐藏物品【一颗很新的光点】是否已到手
    private bool chipsShown;   // 补偿物是否已经浮出来
    private bool revealed;     // 天平真相已揭晓
    private bool finished;     // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第三章：天平 —— 有些重量，天平称不出来");

        layout = DataStore.GetChapter(ChapterId);

        scale = GetNode<BalanceScale>("BalanceScale");
        cardLayer = GetNode<Control>("CardLayer");
        rightPanBtn = GetNode<Button>("Hotspots/RightPanBtn");
        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        rightPanBtn.Pressed += OnRightPanPressed;
        rightPanGlow = HotspotGlow.Attach(rightPanBtn);

        // 第十五轮：热点按"美术图坐标"登记
        ArtAnchor.TrackFraction(rightPanBtn, new Rect2(0.599f, 0.37f, 0.13f, 0.186f));

        // "点万物有回应"：头顶的雾、地上的光、左边的暗处（data/flavor/ch03.json）
        AddChild(FlavorSpots.Create(hotspotsRoot, "ch03"));

        foreach (var def in layout.Cards)
            cardButtons[def.Id] = BuildCard(def);

        UiSounds.WireAll(this);

        // 开场对白放完之前，一切都不能点
        rightPanBtn.Disabled = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 章节专属 BGM（第十一轮：第三/四章各用一首，不再一章一首循环到底）
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym2, -14f, 1.2f);

        // 章节标题缓缓淡出
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 还没放的东西轻轻呼吸</summary>
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
        // 卡片阶段还没放完 → 提示卡片；卡片放完了 → 提示补偿物
        var targets = chipsShown ? (IReadOnlyList<string>)IdsOf(layout.Compensations) : IdsOf(layout.Cards);
        var done = chipsShown ? doneChips : doneCards;
        var buttons = chipsShown ? chipButtons : (IDictionary<string, Button>)cardButtons;

        foreach (var id in targets)
        {
            if (done.Contains(id))
                continue;
            if (!buttons.TryGetValue(id, out var btn) || !IsInstanceValid(btn))
                continue;
            if (!chipsShown && glows.TryGetValue(id, out var glow) && glow != null)
            {
                HotspotGlow.Pulse(glow, delay);
                delay += 0.4f;
            }
        }

        if (chipsShown && !rightPanBtn.Disabled)
            HotspotGlow.Pulse(rightPanGlow, delay);
    }

    private static IReadOnlyList<string> IdsOf(List<CardEntry> cards)
    {
        var list = new List<string>();
        foreach (var c in cards) list.Add(c.Id);
        return list;
    }

    private static IReadOnlyList<string> IdsOf(List<ChipEntry> chips)
    {
        var list = new List<string>();
        foreach (var c in chips) list.Add(c.Id);
        return list;
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
        foreach (var def in layout.Cards)
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

        if (!chipsShown)
        {
            int n = doneCards.Count;
            hintLabel.Text = n >= layout.Cards.Count
                ? DataStore.Text("hint.ch03_full")
                : DataStore.Text("hint.ch03_cards", n, layout.Cards.Count);
            return;
        }

        int m = doneChips.Count;
        hintLabel.Text = m >= layout.Compensations.Count
            ? DataStore.Text("hint.ch03_tried")
            : DataStore.Text("hint.ch03_try", m, layout.Compensations.Count);
    }

    // ==================== 记忆卡片 ====================

    /// <summary>做一张带水彩配图的记忆卡片（图 + 标题 + 副题）</summary>
    private Button BuildCard(CardEntry def)
    {
        float w = layout.CardSize.W;
        float h = layout.CardSize.H;

        var btn = new Button
        {
            Position = new Vector2(def.X, def.Y),
            Size = new Vector2(w, h),
            PivotOffset = new Vector2(w / 2f, h / 2f),
            Scale = new Vector2(0.9f, 0.9f),
            Modulate = new Color(1, 1, 1, 0),
            Disabled = true,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        btn.AddThemeStyleboxOverride("normal", MakePaper(new Color(0.93f, 0.88f, 0.78f), 10));
        btn.AddThemeStyleboxOverride("hover", MakePaper(new Color(0.97f, 0.93f, 0.84f), 18));
        btn.AddThemeStyleboxOverride("pressed", MakePaper(new Color(0.86f, 0.80f, 0.68f), 6));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);

        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        box.AddThemeConstantOverride("separation", 8);

        // 配图：占卡片上半（原图 1536x1024，这里按 3:2 裁切显示，不变形）
        var art = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(w - 20, (h - 66) * 0.78f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var texture = GD.Load<Texture2D>(def.Image);
        if (texture != null)
            art.Texture = texture;
        else
            GD.PrintErr($"[第三章] 卡片配图缺失：{def.Image}");
        box.AddChild(art);

        var title = new Label
        {
            Text = DataStore.Text(def.TitleKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.30f, 0.23f, 0.15f));
        box.AddChild(title);

        var sub = new Label
        {
            Text = DataStore.Text(def.SubKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", new Color(0.47f, 0.39f, 0.28f));
        box.AddChild(sub);

        margin.AddChild(box);
        btn.AddChild(margin);

        // 悬停时轻轻抬起
        btn.MouseEntered += () => TweenCardScale(btn, 1.04f, 0.14f);
        btn.MouseExited += () => TweenCardScale(btn, 1.0f, 0.16f);

        btn.Pressed += () => OnCardPressed(def);
        cardLayer.AddChild(btn);
        glows[def.Id] = HotspotGlow.Attach(btn);
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

    private static void TweenCardScale(Control node, float s, float d)
    {
        if (node.HasMeta("flying"))
            return;
        var tw = node.CreateTween();
        tw.TweenProperty(node, "scale", new Vector2(s, s), d)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void OnCardPressed(CardEntry def)
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

        dm.PlaySequence(ChapterId, $"card_{def.Id}", () => FlyToPan(def.Id, btn, isCard: true));
    }

    // ==================== 补偿物（天平的失败互动） ====================

    /// <summary>三张卡片都放上去了 → 浮出"还能给的"三样东西</summary>
    private void ShowCompensations()
    {
        if (chipsShown)
            return;
        chipsShown = true;

        DialogueManager.Instance.PlaySequence(ChapterId, "cards_done", () =>
        {
            float delay = 0.1f;
            foreach (var def in layout.Compensations)
            {
                var chip = BuildChip(def);
                var tw = CreateTween();
                tw.TweenInterval(delay);
                tw.TweenCallback(Callable.From(() => chip.Disabled = false));
                tw.SetParallel(true);
                tw.TweenProperty(chip, "modulate:a", 1f, 0.45);
                tw.TweenProperty(chip, "scale", Vector2.One, 0.45)
                    .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                delay += 0.16f;
            }
            UpdateHint();
            lastActivityMsec = Time.GetTicksMsec();
        });
    }

    /// <summary>补偿物长什么样：一小条纸片，一行标题 + 一行小字</summary>
    private Button BuildChip(ChipEntry def)
    {
        float w = layout.ChipSize.W;
        float h = layout.ChipSize.H;

        var btn = new Button
        {
            Position = new Vector2(def.X, def.Y),
            Size = new Vector2(w, h),
            PivotOffset = new Vector2(w / 2f, h / 2f),
            Scale = new Vector2(0.92f, 0.92f),
            Modulate = new Color(1, 1, 1, 0),
            Disabled = true,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        // 冷灰色纸：和左边那叠暖白回忆区分开——它们是"后来才想给的"
        btn.AddThemeStyleboxOverride("normal", MakePaper(new Color(0.86f, 0.86f, 0.88f), 8));
        btn.AddThemeStyleboxOverride("hover", MakePaper(new Color(0.93f, 0.93f, 0.95f), 15));
        btn.AddThemeStyleboxOverride("pressed", MakePaper(new Color(0.78f, 0.78f, 0.82f), 5));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var box = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        box.AddThemeConstantOverride("separation", 4);

        var title = new Label
        {
            Text = DataStore.Text(def.TitleKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.28f, 0.26f, 0.3f));
        // 第十五轮：和选项按钮/心声纸片同一款字（楷体 + 字间距），不再是默认样子
        title.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        box.AddChild(title);

        var sub = new Label
        {
            Text = DataStore.Text(def.SubKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        sub.AddThemeFontSizeOverride("font_size", 15);
        sub.AddThemeColorOverride("font_color", new Color(0.45f, 0.44f, 0.5f));
        sub.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        box.AddChild(sub);

        btn.AddChild(box);
        // 第十五轮：Button 不是容器，子节点不会自动铺满——不补这一句，
        // Center 对齐全落空，文字挤在左上角且每行缩进不一致
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        btn.MouseEntered += () => TweenCardScale(btn, 1.05f, 0.14f);
        btn.MouseExited += () => TweenCardScale(btn, 1.0f, 0.16f);
        btn.Pressed += () => OnChipPressed(def);

        cardLayer.AddChild(btn);
        chipButtons[def.Id] = btn;
        return btn;
    }

    private void OnChipPressed(ChipEntry def)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || revealed || doneChips.Contains(def.Id))
            return;
        if (!chipButtons.TryGetValue(def.Id, out var btn) || !IsInstanceValid(btn))
            return;

        lastActivityMsec = Time.GetTicksMsec();
        doneChips.Add(def.Id);
        HotspotGlow.Sparkle(btn);
        UpdateHint();

        dm.PlaySequence(ChapterId, $"comp_{def.Id}", () => FlyToPan(def.Id, btn, isCard: false));
    }

    // ==================== 飞进左盘 ====================

    /// <summary>回忆/补偿放完 → 飞进左盘、缩成一张纸片码上去，天平倾斜</summary>
    private void FlyToPan(string id, Button btn, bool isCard)
    {
        btn.Disabled = true;
        btn.SetMeta("flying", true);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxCardFly, -14f, 0.06f);

        // 纸片角：卡片用数据里的 tilt，补偿物按"第几样"越歪越多
        float tilt = isCard ? TiltForCard(id) : TiltForChip(doneChips.Count);

        Vector2 localTarget = cardLayer.GetGlobalTransform().AffineInverse() * scale.LeftPanDropGlobal();
        Vector2 posTarget = localTarget - btn.Size * 0.5f;

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(btn, "position", posTarget, 0.85)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(btn, "scale", new Vector2(0.28f, 0.28f), 0.85).SetEase(Tween.EaseType.In);
        tw.TweenProperty(btn, "rotation", tilt, 0.85);
        tw.TweenProperty(btn, "modulate:a", 0.0f, 0.6).SetDelay(0.3);
        tw.Chain().TweenCallback(Callable.From(() =>
        {
            btn.QueueFree();
            if (isCard)
            {
                cardButtons.Remove(id);
                GameManager.Instance.AddItem(id); // 第十五轮：放上天平的记忆卡片，收一份进背包
            }
            else chipButtons.Remove(id);

            if (isCard) scale.AddMemoryChip();
            else scale.AddCompensationChip();

            AudioManager.Instance?.PlaySfx(AudioManager.SfxItemPlace, -12f, 0.05f);

            int n = isCard ? doneCards.Count : doneChips.Count;
            scale.SetTilt(isCard ? TiltAfterCards(n) : TiltAfterChips(n));
            lastActivityMsec = Time.GetTicksMsec();

            if (isCard && n >= layout.Cards.Count)
            {
                GetTree().CreateTimer(0.9).Timeout += ShowCompensations;
            }
            else if (!isCard && n >= layout.Compensations.Count)
            {
                // 能给的都给完了，还是没平衡 → 先让玩家听见这一句，再揭晓真相
                GetTree().CreateTimer(0.9).Timeout += PlayChipsFailed;
            }
        }));
    }

    private float TiltForCard(string id)
    {
        var def = layout.Cards.Find(c => c.Id == id);
        return def?.Tilt ?? 0f;
    }

    private float TiltForChip(int count) => -0.05f * count;

    /// <summary>放完第 n 张卡片后的倾角（数据里没写就用老数字兜底）</summary>
    private float TiltAfterCards(int n)
    {
        int i = Mathf.Clamp(n - 1, 0, layout.TiltAfterCards.Count - 1);
        return layout.TiltAfterCards.Count > 0 ? layout.TiltAfterCards[i] : -7f * n;
    }

    /// <summary>放完第 n 样补偿物后的倾角——一样比一样歪得更厉害</summary>
    private float TiltAfterChips(int n)
    {
        int i = Mathf.Clamp(n - 1, 0, layout.TiltAfterChips.Count - 1);
        return layout.TiltAfterChips.Count > 0 ? layout.TiltAfterChips[i] : -24f - 6f * n;
    }

    // ==================== 右盘彩蛋 ====================

    private void OnRightPanPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || revealed)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(rightPanBtn);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -12f, 0.05f);

        rightPanStage++;
        string seq = rightPanStage switch
        {
            1 => "right_pan_1",
            2 => "right_pan_2",
            _ => "right_pan_more",
        };

        // 第二次认真看那颗光点 → 收到隐藏物品【一颗很新的光点】
        bool award = rightPanStage >= 2 && !orbAwarded;
        dm.PlaySequence(ChapterId, seq, () =>
        {
            if (award)
            {
                orbAwarded = true;
                FindHiddenItem("light_orb");
            }
        });
    }

    // ==================== 揭晓 → 选择 → 尾声 ====================

    /// <summary>三样补偿物都加上了，天平还是那副样子 → 说一句"做不到"，再进揭晓</summary>
    private void PlayChipsFailed()
    {
        if (revealed || finished)
            return;
        DialogueManager.Instance.PlaySequence(ChapterId, "chips_failed", PlayReveal);
    }

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
        // "手机活起来"：分手的话说出口，老家的发小正好发来"啥时候回来"
        LiveEvents.Fire(this, ChapterId, "ch03_faxiao_drink");

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
