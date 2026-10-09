using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 背包（第十五轮）
///
/// 【这是什么】
/// 章节界面左上角多了一个"背包"按钮，点开是一块纸质感的格子面板：
/// 手机、一路捡到的东西、藏得很深的隐藏物品，都躺在格子里。
/// 点一件东西 → 翻到它的"里侧"：放大图 + 一句描述；手机还能从这里直接点开。
///
/// 【数据在哪】
///   data/inventory.json —— 图鉴（id + 名字/描述文案键 + 图标 + 动作）
///   玩家真正"拥有哪些"存在存档里（GameManager.InventoryItems）
///
/// 【小红点】
/// 新拿到的东西没被看过时，背包按钮右上角会亮一个小红点；
/// 打开一次背包（MarkBagSeen）就消掉，而且会存档 —— 下次进游戏不会又冒出来。
/// 格子上还会给"这次才看到的东西"标一个小圆点，方便一眼认出新东西。
///
/// 【和手机的关系】
/// 移动端全屏模态界面（手机）那套"输入抑制"在这里照搬：
/// 背包打开期间 DialogueManager.SetUiSuppressed(true)，背景对话/热点全部锁死。
/// </summary>
public partial class Backpack : Control
{
    /// <summary>面板收起（fade 结束、即将销毁）时触发</summary>
    public event Action Closed;

    // ---------- 尺寸与排版 ----------
    private const float PanelW = 1180f;
    private const float PanelH = 700f;
    private const int Columns = 4;
    private const int MinSlots = 8;

    // ---------- 纸质暖色系（和选项纸片/天平一个家族） ----------
    private static readonly Color PaperBg = new(0.968f, 0.945f, 0.892f);
    private static readonly Color PaperBorder = new(0.80f, 0.70f, 0.52f);
    private static readonly Color SlotBg = new(0.995f, 0.975f, 0.925f);
    private static readonly Color SlotBorder = new(0.78f, 0.68f, 0.50f);
    private static readonly Color SlotHoverBg = new(1f, 0.995f, 0.965f);
    private static readonly Color SlotPressedBg = new(0.945f, 0.915f, 0.845f);
    private static readonly Color EmptySlotBg = new(0.93f, 0.905f, 0.85f);
    private static readonly Color InkDark = new(0.30f, 0.22f, 0.14f);
    private static readonly Color InkMid = new(0.42f, 0.34f, 0.25f);
    private static readonly Color InkFaint = new(0.55f, 0.48f, 0.38f);
    private static readonly Color HiddenTagColor = new(0.72f, 0.50f, 0.22f);

    // ---------- 节点 ----------
    private Control panelRoot;
    private Control gridWrap;
    private GridContainer grid;
    private Control detailView;
    private TextureRect detailIcon;
    private Label detailName;
    private Label detailTag;
    private Label detailDesc;
    private Button detailAction;
    private Label footerLabel;

    // ---------- 状态 ----------
    private string selectedId = "";
    private bool closing;
    private bool suppressing;
    private bool opened;

    // ==================== 搭建 ====================

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        BuildUi();
        UiSounds.WireAll(this);
    }

    private void BuildUi()
    {
        // 暗幕：点空白处合上背包
        var scrim = new ColorRect
        {
            Color = new Color(0.06f, 0.05f, 0.04f, 0.55f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scrim.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                AcceptEvent();
                Close();
            }
        };
        AddChild(scrim);

        // 面板：屏幕正中的一块"牛皮纸板"
        panelRoot = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panelRoot.AnchorLeft = 0.5f; panelRoot.AnchorRight = 0.5f;
        panelRoot.AnchorTop = 0.5f; panelRoot.AnchorBottom = 0.5f;
        panelRoot.OffsetLeft = -PanelW / 2f; panelRoot.OffsetRight = PanelW / 2f;
        panelRoot.OffsetTop = -PanelH / 2f; panelRoot.OffsetBottom = PanelH / 2f;
        panelRoot.PivotOffset = new Vector2(PanelW / 2f, PanelH / 2f);
        AddChild(panelRoot);

        var panel = new PanelContainer();
        var panelStyle = new StyleBoxFlat
        {
            BgColor = PaperBg,
            BorderColor = PaperBorder,
            BorderWidthTop = 3,
            BorderWidthBottom = 3,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            CornerRadiusTopLeft = 24,
            CornerRadiusTopRight = 24,
            CornerRadiusBottomLeft = 24,
            CornerRadiusBottomRight = 24,
            ContentMarginLeft = 34,
            ContentMarginRight = 34,
            ContentMarginTop = 26,
            ContentMarginBottom = 22,
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowSize = 18,
            ShadowOffset = new Vector2(0, 8),
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panelRoot.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        // ---- 顶栏：标题 + 关闭 ----
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        column.AddChild(header);

        var title = new Label { Text = DataStore.Text("bag.title") };
        title.AddThemeFontSizeOverride("font_size", 40);
        title.AddThemeColorOverride("font_color", InkDark);
        header.AddChild(title);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);

        var closeBtn = new Button
        {
            Text = "×",
            CustomMinimumSize = new Vector2(52, 52),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = DataStore.Text("bag.close_tip"),
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 32);
        closeBtn.AddThemeColorOverride("font_color", InkMid);
        closeBtn.AddThemeColorOverride("font_hover_color", new Color(0.80f, 0.42f, 0.22f));
        closeBtn.AddThemeStyleboxOverride("normal", FlatStyle(new Color(0, 0, 0, 0), 14));
        closeBtn.AddThemeStyleboxOverride("hover", FlatStyle(new Color(0.85f, 0.76f, 0.60f, 0.45f), 14));
        closeBtn.AddThemeStyleboxOverride("pressed", FlatStyle(new Color(0.80f, 0.68f, 0.50f, 0.6f), 14));
        closeBtn.Pressed += Close;
        header.AddChild(closeBtn);

        // ---- 中段：格子视图 / 详情视图（二选一显示） ----
        var body = new Control
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddChild(body);

        gridWrap = new Control { MouseFilter = MouseFilterEnum.Ignore };
        gridWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.AddChild(gridWrap);

        var gridCenter = new CenterContainer();
        gridCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        gridWrap.AddChild(gridCenter);

        grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 20);
        grid.AddThemeConstantOverride("v_separation", 18);
        gridCenter.AddChild(grid);

        detailView = BuildDetailView();
        body.AddChild(detailView);

        // ---- 底栏：一句小提示 ----
        footerLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        footerLabel.AddThemeFontSizeOverride("font_size", 20);
        footerLabel.AddThemeColorOverride("font_color", InkFaint);
        column.AddChild(footerLabel);
    }

    private Control BuildDetailView()
    {
        var root = new Control
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        detailIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(440, 258),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(detailIcon);

        detailName = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        detailName.AddThemeFontSizeOverride("font_size", 40);
        detailName.AddThemeColorOverride("font_color", InkDark);
        box.AddChild(detailName);

        detailTag = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        detailTag.AddThemeFontSizeOverride("font_size", 20);
        detailTag.AddThemeColorOverride("font_color", HiddenTagColor);
        box.AddChild(detailTag);

        detailDesc = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            CustomMinimumSize = new Vector2(660, 64),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        detailDesc.AddThemeFontSizeOverride("font_size", 26);
        detailDesc.AddThemeColorOverride("font_color", InkMid);
        box.AddChild(detailDesc);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        buttons.AddThemeConstantOverride("separation", 18);
        box.AddChild(buttons);

        detailAction = new Button
        {
            Text = DataStore.Text("bag.open_phone"),
            CustomMinimumSize = new Vector2(240, 56),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            Visible = false,
        };
        detailAction.AddThemeFontSizeOverride("font_size", 24);
        detailAction.AddThemeStyleboxOverride("normal", FlatStyle(new Color(0.831f, 0.647f, 0.455f), 14));
        detailAction.AddThemeStyleboxOverride("hover", FlatStyle(new Color(0.910f, 0.769f, 0.604f), 14));
        detailAction.AddThemeStyleboxOverride("pressed", FlatStyle(new Color(0.722f, 0.537f, 0.306f), 14));
        detailAction.Pressed += OnDetailAction;
        buttons.AddChild(detailAction);

        var backBtn = new Button
        {
            Text = DataStore.Text("bag.back"),
            CustomMinimumSize = new Vector2(180, 56),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        backBtn.AddThemeFontSizeOverride("font_size", 24);
        backBtn.AddThemeColorOverride("font_color", InkDark);
        backBtn.AddThemeStyleboxOverride("normal", FlatStyle(new Color(0.90f, 0.86f, 0.78f), 14));
        backBtn.AddThemeStyleboxOverride("hover", FlatStyle(new Color(0.945f, 0.91f, 0.845f), 14));
        backBtn.AddThemeStyleboxOverride("pressed", FlatStyle(new Color(0.82f, 0.77f, 0.68f), 14));
        backBtn.Pressed += ShowGrid;
        buttons.AddChild(backBtn);

        return root;
    }

    private static StyleBoxFlat FlatStyle(Color bg, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };
    }

    // ==================== 开合 ====================

    /// <summary>打开背包（挂进场景树后调用）</summary>
    public void Open()
    {
        if (opened)
            return;
        opened = true;

        // 先记下"这次新看到的"，再标记全部看过 —— 格子上给它们留个小圆点
        var fresh = new List<string>();
        foreach (var id in GameManager.Instance?.InventoryItems ?? (IReadOnlyCollection<string>)new List<string>())
            if (!GameManager.Instance.HasBagSeen(id))
                fresh.Add(id);
        GameManager.Instance?.MarkBagSeen();

        RebuildGrid(fresh);
        ShowGrid();

        // 手机端同款处理：背包开着的时候，背景对话/热点全部锁死
        suppressing = true;
        DialogueManager.Instance?.SetUiSuppressed(true);

        Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 1f, 0.2).SetEase(Tween.EaseType.Out);
    }

    /// <summary>合上背包（fade 后销毁，并通知外面）</summary>
    public void Close()
    {
        if (closing)
            return;
        closing = true;

        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, 0.16).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(() =>
        {
            if (suppressing)
            {
                suppressing = false;
                DialogueManager.Instance?.SetUiSuppressed(false);
            }
            Closed?.Invoke();
            QueueFree();
        }));
    }

    public override void _ExitTree()
    {
        // 兜底：万一没走 Close 就被销毁（切场景），别把背景对话的输入锁死
        if (suppressing && !closing)
        {
            suppressing = false;
            DialogueManager.Instance?.SetUiSuppressed(false);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) // Esc / 手柄 B
        {
            GetViewport().SetInputAsHandled();
            if (detailView.Visible)
                ShowGrid();
            else
                Close();
        }
    }

    // ==================== 视图 ====================

    private void ShowGrid()
    {
        gridWrap.Visible = true;
        detailView.Visible = false;
        selectedId = "";
        footerLabel.Text = DataStore.Text("bag.hint");
    }

    private void ShowDetail(string itemId)
    {
        selectedId = itemId;
        var def = DataStore.ItemDef(itemId);

        detailIcon.Texture = LoadIcon(def);
        detailIcon.Modulate = Colors.White;
        detailName.Text = DataStore.ItemName(itemId);

        detailTag.Visible = DataStore.IsHiddenItem(itemId);
        if (detailTag.Visible)
            detailTag.Text = DataStore.Text("bag.tag_hidden");

        detailDesc.Text = def != null && !string.IsNullOrEmpty(def.DescKey)
            ? DataStore.Text(def.DescKey)
            : "";

        detailAction.Visible = def != null && def.Action == "phone";

        gridWrap.Visible = false;
        detailView.Visible = true;
        footerLabel.Text = DataStore.Text("bag.hint_detail");
    }

    private void RebuildGrid(List<string> fresh)
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }

        int filled = 0;
        foreach (var def in DataStore.InventoryRegistry)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
                continue;
            if (GameManager.Instance?.HasItem(def.Id) != true)
                continue;
            grid.AddChild(MakeSlotButton(def, fresh != null && fresh.Contains(def.Id)));
            filled++;
        }

        // 空格子：淡淡的凹槽，让"还没集齐"一眼可见
        for (int i = filled; i < MinSlots; i++)
            grid.AddChild(MakeEmptySlot());
    }

    private Button MakeSlotButton(InventoryItemEntry def, bool isFresh)
    {
        var btn = new Button
        {
            CustomMinimumSize = new Vector2(256, 224),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeStyleboxOverride("normal", FlatStyle(SlotBg, 16));
        btn.AddThemeStyleboxOverride("hover", HoverSlotStyle());
        btn.AddThemeStyleboxOverride("pressed", FlatStyle(SlotPressedBg, 16));
        btn.Pressed += () => ShowDetail(def.Id);
        UiSounds.Wire(btn); // 格子是打开背包时才建的，赶不上 _Ready 里的统一接线

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.OffsetLeft = 14;
        column.OffsetRight = -14;
        column.OffsetTop = 10;
        column.OffsetBottom = -10;
        column.AddThemeConstantOverride("separation", 6);
        btn.AddChild(column);

        var icon = new TextureRect
        {
            Texture = LoadIcon(def),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(icon);

        var name = new Label
        {
            Text = DataStore.ItemName(def.Id),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 22);
        name.AddThemeColorOverride("font_color", InkDark);
        column.AddChild(name);

        // "这次才看到"的小圆点（打开背包的那一刻就不再出现）
        if (isFresh)
        {
            var dot = new Label
            {
                Text = "●",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            dot.AddThemeFontSizeOverride("font_size", 18);
            dot.AddThemeColorOverride("font_color", new Color(0.90f, 0.44f, 0.20f));
            dot.AnchorLeft = 1f;
            dot.AnchorRight = 1f;
            dot.AnchorTop = 0f;
            dot.AnchorBottom = 0f;
            dot.OffsetLeft = -30;
            dot.OffsetTop = 6;
            btn.AddChild(dot);
        }

        return btn;
    }

    private StyleBoxFlat HoverSlotStyle()
    {
        var style = FlatStyle(SlotHoverBg, 16);
        style.BorderColor = SlotBorder;
        style.BorderWidthTop = 2;
        style.BorderWidthBottom = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        return style;
    }

    private PanelContainer MakeEmptySlot()
    {
        var slot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(256, 224),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(EmptySlotBg, 0.55f),
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16,
            CornerRadiusBottomRight = 16,
        };
        slot.AddThemeStyleboxOverride("panel", style);
        return slot;
    }

    // ==================== 动作 ====================

    /// <summary>详情页的"打开手机"：先合上背包，再把手机掏出来</summary>
    private void OnDetailAction()
    {
        var def = DataStore.ItemDef(selectedId);
        if (def == null || def.Action != "phone")
            return;

        var parent = GetParent();
        Closed += () =>
        {
            if (parent == null || !IsInstanceValid(parent))
                return;
            var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
                .Instantiate<ChatOverlay>();
            parent.AddChild(overlay);
            overlay.Open(""); // 空字符串 = 掏手机（先落在锁屏，解锁后进桌面/微信）
        };
        Close();
    }

    private static Texture2D LoadIcon(InventoryItemEntry def)
    {
        if (def == null || string.IsNullOrEmpty(def.Icon))
            return null;
        if (!ResourceLoader.Exists(def.Icon))
        {
            GD.PrintErr($"[背包] 找不到图标：{def.Icon}");
            return null;
        }
        return GD.Load<Texture2D>(def.Icon);
    }

    // ==================== 杂项 ====================

    /// <summary>屏幕比面板还小时整体等比缩小（手机框那套逻辑）</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationResized || what == NotificationReady)
        {
            if (panelRoot == null || !IsInsideTree())
                return;
            var vp = GetViewportRect().Size;
            float s = Mathf.Min(1f, Mathf.Min((vp.X - 48f) / PanelW, (vp.Y - 48f) / PanelH));
            panelRoot.Scale = new Vector2(s, s);
        }
    }
}

/// <summary>
/// 章节 HUD 按钮上的"有新东西"小红点：自己每半秒看一眼 GameManager.HasNewBagItems。
/// （和手机热点的未读小红点长得一样，各盯各的。）
/// </summary>
public partial class ChapterHudDot : Control
{
    private ulong lastPoll;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -18;
        OffsetTop = -6;
        OffsetRight = 16;
        OffsetBottom = 28;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - lastPoll < 500)
            return;
        lastPoll = Time.GetTicksMsec();
        bool show = GameManager.Instance?.HasNewBagItems == true;
        if (show != Visible)
        {
            Visible = show;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var c = Size / 2f;
        DrawCircle(c, Mathf.Min(Size.X, Size.Y) / 2f, new Color(0.92f, 0.25f, 0.20f));
        DrawCircle(c, Mathf.Min(Size.X, Size.Y) / 2f - 2.5f, new Color(1f, 0.45f, 0.4f, 0.55f));
    }
}
