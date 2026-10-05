using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 手机微信界面（引擎绘制版 · 完整微信框架）
///
/// 【为什么用"引擎绘制"而不是一张 AI 生成的聊天截图】
///   - 中文清晰锐利，不管什么分辨率都不会糊
///   - 聊天 / 联系人 / 朋友圈内容全在 data/*.json 里，改文案不碰代码
///   - 可以真的"往上滑"，玩家能一条一条翻旧消息（剧情里的动作）
///
/// 【结构】手机里有三个"页面"，靠返回键/标签切换：
///   ChatOverlay（本场景 = 手机外壳：黑幕 + 手机壳 + 收起按钮）
///   └─ PhonePanel
///      ├─ chatPage    聊天页（顶栏返回/名字/··· + 消息流 + 底部输入栏）
///      ├─ mainPage    微信主框架（底部四标签：微信/通讯录/发现/我）
///      ├─ momentsPage 朋友圈（封面 + 动态 + 点赞 + 评论）
///      └─ toast       居中小提示
///
/// 导航规则（和真微信一致）：
///   聊天页 ‹  → 主框架；发现页朋友圈 → 朋友圈；朋友圈 ‹ → 主框架"发现"
///   聊天页 ··· → 微信式下拉菜单（发起通话 / 清空聊天记录 / 收起手机）
///
/// 用法：
///   var overlay = scene.Instantiate&lt;ChatOverlay&gt;();
///   AddChild(overlay);
///   overlay.Closed += 关掉之后要做的事;
///   overlay.Open("ch02_phone");   // 对应 data/contacts.json 里 ChatFile = ch02_phone 的联系人
/// </summary>
public partial class ChatOverlay : Control
{
    /// <summary>界面收起（fade 结束、即将销毁）时触发</summary>
    public event Action Closed;

    // ---------- 微信风配色 ----------
    private static readonly Color MyBubbleColor = new Color(0.585f, 0.925f, 0.41f); // 我：微信绿
    private static readonly Color HerBubbleColor = new Color(1f, 1f, 1f);           // 对方：白
    private static readonly Color TextColor = new Color(0.09f, 0.09f, 0.11f);
    private static readonly Color DividerColor = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color HeaderBgColor = new Color(0.925f, 0.925f, 0.935f); // 微信顶栏浅灰
    private static readonly Color HeaderTextColor = new Color(0.12f, 0.12f, 0.14f);
    private static readonly Color TimestampColor = new Color(0.55f, 0.55f, 0.58f);

    private const int FontSize = 28;
    private const float MaxTextBubbleWidth = 540f; // 气泡最宽（超过就换行）
    private const float ImageBubbleWidth = 380f;

    // 遮罩着色器只加载一次（静态缓存）
    private static Shader roundedMaskShader;

    /// <summary>被"清空聊天记录"清过的会话（本局内有效）</summary>
    private static readonly HashSet<string> clearedChats = new();

    // ---------- 节点 ----------
    private Panel phonePanel;
    private Control pageHost;   // 手机内容层（底部留出"放下手机"横条）
    private Control chatPage;
    private WeChatMainPage mainPage;
    private MomentsPage momentsPage;
    private SubscriptionsPage subscriptionsPage; // 订阅号消息（第九轮）
    private StepsRankingPage stepsPage;          // 微信运动排行（第九轮）

    // 聊天页节点
    private ScrollContainer scroll;
    private VBoxContainer rows;
    private Control hintChip;
    private Label nameLabel;
    private Control menuCatcher;
    private PanelContainer menuPanel;
    private LineEdit inputField;
    private Control plusPanel;
    private Control quickBar;
    private PanelContainer typingChip; // "对方正在输入…"指示
    private Tween hintFadeTween;       // 提示气泡自动淡出
    private Tween hintReturnTween;     // 提示演示完滑回最新消息

    // toast
    private PanelContainer toastChip;
    private Label toastLabel;
    private Tween toastTween;

    private ContactData currentContact;
    private ChatScriptData currentChatData; // 当前会话的 JSON 剧本（只读 / 幽灵打字等标记都在这）
    private Tween ghostTween;               // 幽灵打字动画（第四章"手滑"）

    /// <summary>当前会话是否只读（联系人级 ReadOnly 或剧本级 readOnly 任一成立）</summary>
    private bool ActiveReadOnly => (currentContact?.ReadOnly ?? false) || (currentChatData?.ReadOnly ?? false);

    private bool closing;    // 正在收起（防止重复触发）
    private bool suppressing; // 正在抑制背景对话输入
    private bool hintArmed;  // "滑一滑"提示是否已武装（入场滚动不算）
    private int messageIndex; // 消息序号（用于生成时间戳）
    private bool lastRowWasTimeInfo; // 上一行是分割线/时间戳 → 不再叠时间戳

    /// <summary>本局内追加发送过的消息（联系人 id → 追加列表），重开手机还在</summary>
    private static readonly Dictionary<string, List<ChatMessageData>> extraMessages = new();

    /// <summary>已经用过预设回复的会话（预设条只出现一次）</summary>
    private static readonly HashSet<string> usedQuickReplies = new();

    /// <summary>「往上滑」提示已经亮过的次数（全程只提示前 2 次）</summary>
    private static int hintShownCount;

    public override void _Ready()
    {
        phonePanel = GetNode<Panel>("PhonePanel");

        // 所有页面放在内容层里，底部 56px 留给手机框内的"放下手机"横条
        pageHost = new Control { MouseFilter = MouseFilterEnum.Ignore };
        pageHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.OffsetBottom = -56;
        phonePanel.AddChild(pageHost);

        BuildBottomBar();
    }

    public override void _ExitTree()
    {
        // 兜底：万一没走 Close 就被销毁（切场景），别把背景对话的输入锁死
        if (suppressing)
        {
            suppressing = false;
            DialogueManager.Instance?.SetUiSuppressed(false);
        }
    }

    // ==================== 对外 API ====================

    /// <summary>
    /// 打开手机，直接进入 chatId 对应的聊天（剧情入口，和以前用法一致）
    /// </summary>
    public void Open(string chatId)
    {
        var contacts = WeChatData.LoadContacts();

        BuildChatPage();
        BuildMainPage(contacts);
        BuildMomentsPage();
        BuildSystemPages();
        BuildToast();

        // 页面搭好之后再统一接按钮音效
        UiSounds.WireAll(this);

        // 找到 chatId 对应的联系人（按 ChatFile 或 id 匹配）
        var contact = contacts.Contacts.Find(c => c.ChatFile == chatId || c.Id == chatId)
                      ?? new ContactData
                      {
                          Id = chatId,
                          Name = "宝宝（前女友）",
                          Avatar = WeChatData.BaobaoAvatarPath,
                          ChatFile = chatId,
                      };

        // 手机比窗口还大时整体等比缩小，保证整台手机（含底部横条）永远完整可见
        var vp = GetViewportRect().Size;
        float s = Mathf.Min(1f, Mathf.Min((vp.Y - 32f) / 972f, (vp.X - 32f) / 760f));
        if (s < 0.999f)
        {
            phonePanel.PivotOffset = phonePanel.Size * 0.5f;
            phonePanel.Scale = new Vector2(s, s);
        }

        ShowChat(contact);

        // 手机打开期间：背景对话/热点全部锁死，点击只属于手机
        suppressing = true;
        DialogueManager.Instance?.SetUiSuppressed(true);

        // 入场动画：整体淡入 + 手机从下方轻轻滑上来
        Modulate = new Color(1, 1, 1, 0);
        PlayEnterAnimation();
    }

    /// <summary>收起手机（fade 后销毁，并通知外面）</summary>
    public void Close()
    {
        if (closing)
            return;
        closing = true;

        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.18);
        tween.TweenCallback(Callable.From(() =>
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) // Esc / 手柄 B
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    // ==================== 页面切换 ====================

    private void SetPage(Control page)
    {
        chatPage.Visible = page == chatPage;
        mainPage.Visible = page == mainPage;
        momentsPage.Visible = page == momentsPage;
        if (subscriptionsPage != null) subscriptionsPage.Visible = page == subscriptionsPage;
        if (stepsPage != null) stepsPage.Visible = page == stepsPage;
    }

    /// <summary>打开某位联系人 / 群聊 / 只读会话</summary>
    private void ShowChat(ContactData contact)
    {
        currentContact = contact;
        currentChatData = !string.IsNullOrEmpty(contact.ChatFile) ? LoadChatData(contact.ChatFile) : null;
        ghostTween?.Kill();
        nameLabel.Text = contact.Members > 0 ? $"{contact.Name}（{contact.Members}）" : contact.Name;
        ApplyReadOnlyMode(contact);
        if (ActiveReadOnly)
            inputField.Text = ""; // 只读会话不允许残留草稿（幽灵打字从空白开始）
        HideMenu();
        HideTypingChip();
        RebuildChatRows();
        SetPage(chatPage);
        ScrollToBottom();

        // 「往上滑」提示 + 落点（顶部/底部）统一交给 ArrangeHintAndScroll 决定
        hintArmed = false;
        hintChip.Visible = false;
        hintChip.Modulate = Colors.White;
        hintFadeTween?.Kill();
        hintReturnTween?.Kill();
        GetTree().CreateTimer(1.2).Timeout += () => hintArmed = true;
        ArrangeHintAndScroll();
        MaybePlayGhostTyping();
    }

    /// <summary>
    /// 「往上滑」提示与初始滚动位置（出场条件全面收紧）：
    /// ①这条聊天记录确实能往上翻（内容超出可视区）才显示，短的占位聊天永远不再出现；
    /// ②提示出现时先把记录瞬移到顶部——顶部 64px 留白正好托住气泡，永远不压消息文字，
    ///   也顺便让玩家亲眼看到"上面还有更早的聊天"，2 秒后自动平滑滑回最新消息；
    /// ③玩家自己一滚动立即淡出；
    /// ④全程只提示前 2 次（切多少个联系人也不再骚扰）。
    /// </summary>
    private void ArrangeHintAndScroll()
    {
        string contactId = currentContact.Id;
        GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (closing || !IsInsideTree() || currentContact?.Id != contactId)
                return;
            bool scrollable = scroll.GetVScrollBar().MaxValue > scroll.Size.Y + 1f;
            if (!scrollable || hintShownCount >= 2 || clearedChats.Contains(contactId))
            {
                ScrollToBottom();
                return;
            }

            hintShownCount++;
            scroll.ScrollVertical = 0; // 瞬移到顶部，露出留白区（不压任何消息）
            hintChip.Visible = true;
            hintChip.Modulate = Colors.White;

            hintFadeTween?.Kill();
            hintFadeTween = CreateTween();
            hintFadeTween.TweenInterval(2.0);
            hintFadeTween.TweenProperty(hintChip, "modulate:a", 0f, 0.4);
            hintFadeTween.TweenCallback(Callable.From(() => hintChip.Visible = false));

            hintReturnTween?.Kill();
            hintReturnTween = CreateTween();
            hintReturnTween.TweenInterval(2.1);
            hintReturnTween.TweenProperty(scroll, "scroll_vertical", (int)scroll.GetVScrollBar().MaxValue, 0.8)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        };
    }

    /// <summary>回到微信主框架</summary>
    private void ShowMain()
    {
        HideMenu();
        SetPage(mainPage);
    }

    /// <summary>按当前联系人重建消息流</summary>
    private void RebuildChatRows()
    {
        foreach (var child in rows.GetChildren())
            child.QueueFree();
        messageIndex = 0;
        lastRowWasTimeInfo = false;
        HideQuickBar();
        HidePlusPanel();

        if (clearedChats.Contains(currentContact.Id))
        {
            rows.AddChild(MakeDivider("聊天记录已清空"));
            return;
        }

        var chatData = currentChatData; // ShowChat 时已按当前会话加载好
        List<ChatMessageData> messages = chatData != null ? chatData.Messages : currentContact.Messages;

        foreach (var msg in messages)
            rows.AddChild(MakeMessageRow(msg));

        // 本局内追加发送过的消息（重开手机还在）
        if (extraMessages.TryGetValue(currentContact.Id, out var extra))
            foreach (var msg in extra)
                rows.AddChild(MakeMessageRow(msg));

        // 预设回复条：只在有数据、没用过、没清空的会话里浮出
        if (chatData?.QuickReplies is { Count: > 0 } && !usedQuickReplies.Contains(currentContact.Id))
            BuildQuickBar(chatData.QuickReplies);
    }

    // ==================== 搭建：聊天页 ====================

    private void BuildChatPage()
    {
        chatPage = new Control { MouseFilter = MouseFilterEnum.Stop };
        chatPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.AddChild(chatPage);

        // ---- 顶栏（返回 / 名字 / ···）----
        var header = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var headerStyle = new StyleBoxFlat
        {
            BgColor = HeaderBgColor,
            CornerRadiusTopLeft = 28,
            CornerRadiusTopRight = 28,
        };
        header.AddThemeStyleboxOverride("panel", headerStyle);
        header.AnchorRight = 1;
        header.OffsetBottom = 100;
        header.GrowHorizontal = GrowDirection.Both;
        chatPage.AddChild(header);

        // PanelContainer 会把子节点拉伸铺满，所以中间垫一层 Control 再做定位
        var headerBox = new Control { MouseFilter = MouseFilterEnum.Ignore };
        headerBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChild(headerBox);

        var backBtn = MakeHeaderButton("‹", 44);
        backBtn.OffsetLeft = 20;
        backBtn.OffsetTop = 8;
        backBtn.OffsetRight = 84;
        backBtn.OffsetBottom = 92;
        backBtn.Pressed += ShowMain;
        headerBox.AddChild(backBtn);

        nameLabel = new Label
        {
            Text = "同桌",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 30);
        nameLabel.AddThemeColorOverride("font_color", HeaderTextColor);
        nameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        nameLabel.OffsetLeft = 100;
        nameLabel.OffsetRight = -100;
        headerBox.AddChild(nameLabel);

        var moreBtn = MakeHeaderButton("···", 36);
        moreBtn.AnchorLeft = 1;
        moreBtn.AnchorRight = 1;
        moreBtn.OffsetLeft = -96;
        moreBtn.OffsetTop = 14;
        moreBtn.OffsetRight = -22;
        moreBtn.OffsetBottom = 86;
        moreBtn.GrowHorizontal = GrowDirection.Begin;
        moreBtn.Pressed += ToggleMenu;
        headerBox.AddChild(moreBtn);

        // 顶栏下沿细线
        var headerLine = new ColorRect { Color = new Color(0, 0, 0, 0.07f), MouseFilter = MouseFilterEnum.Ignore };
        headerLine.AnchorRight = 1;
        headerLine.OffsetTop = 100;
        headerLine.OffsetBottom = 102;
        headerLine.GrowHorizontal = GrowDirection.Both;
        chatPage.AddChild(headerLine);

        // ---- 消息滚动区 ----
        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AnchorRight = 1;
        scroll.AnchorBottom = 1;
        scroll.OffsetTop = 102;
        scroll.OffsetBottom = -104;
        scroll.GrowHorizontal = GrowDirection.Both;
        scroll.GrowVertical = GrowDirection.Both;
        chatPage.AddChild(scroll);

        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pad.AddThemeConstantOverride("margin_left", 26);
        pad.AddThemeConstantOverride("margin_right", 26);
        // 顶部留白加大：给"往上滑"提示气泡腾位置，让它浮在空白里、永远不压住消息文字
        pad.AddThemeConstantOverride("margin_top", 64);
        pad.AddThemeConstantOverride("margin_bottom", 28);
        scroll.AddChild(pad);

        rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 16);
        pad.AddChild(rows);

        scroll.GetVScrollBar().ValueChanged += OnScrolled;

        // ---- 底部输入栏（装饰）----
        var footerLine = new ColorRect { Color = new Color(0, 0, 0, 0.07f), MouseFilter = MouseFilterEnum.Ignore };
        footerLine.AnchorTop = 1;
        footerLine.AnchorRight = 1;
        footerLine.AnchorBottom = 1;
        footerLine.OffsetTop = -104;
        footerLine.OffsetBottom = -102;
        footerLine.GrowHorizontal = GrowDirection.Both;
        footerLine.GrowVertical = GrowDirection.Begin;
        chatPage.AddChild(footerLine);

        var footer = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var footerStyle = new StyleBoxFlat
        {
            BgColor = HeaderBgColor,
            // 底部圆角交给手机框内的"放下手机"横条，这里做方角衔接
        };
        footer.AddThemeStyleboxOverride("panel", footerStyle);
        footer.AnchorTop = 1;
        footer.AnchorRight = 1;
        footer.AnchorBottom = 1;
        footer.OffsetTop = -102;
        footer.GrowHorizontal = GrowDirection.Both;
        footer.GrowVertical = GrowDirection.Begin;
        chatPage.AddChild(footer);

        var footerRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        footerRow.AddThemeConstantOverride("separation", 16);
        footer.AddChild(UiKit.WrapMargin(footerRow, 24, 18, 24, 18));

        // ＋ 圆圈按钮：弹出微信式功能面板
        var plusBtn = new Button
        {
            Text = "＋",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(64, 64),
        };
        var plusNormal = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.92f) };
        plusNormal.SetCornerRadiusAll(32);
        var plusHover = new StyleBoxFlat { BgColor = new Color(0.92f, 0.92f, 0.93f) };
        plusHover.SetCornerRadiusAll(32);
        var plusPressed = new StyleBoxFlat { BgColor = new Color(0.85f, 0.85f, 0.87f) };
        plusPressed.SetCornerRadiusAll(32);
        plusBtn.AddThemeStyleboxOverride("normal", plusNormal);
        plusBtn.AddThemeStyleboxOverride("hover", plusHover);
        plusBtn.AddThemeStyleboxOverride("pressed", plusPressed);
        plusBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        plusBtn.AddThemeFontSizeOverride("font_size", 34);
        plusBtn.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.33f));
        plusBtn.Pressed += TogglePlusPanel;
        footerRow.AddChild(plusBtn);

        // 输入框：真能打字（回车 = 发送）
        inputField = new LineEdit
        {
            PlaceholderText = "发消息……",
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 64),
            ContextMenuEnabled = false,
            ShortcutKeysEnabled = true,
        };
        var inputStyle = new StyleBoxFlat { BgColor = new Color(0.96f, 0.96f, 0.97f) };
        inputStyle.SetCornerRadiusAll(10);
        inputStyle.SetBorderWidthAll(1);
        inputStyle.BorderColor = new Color(0.85f, 0.85f, 0.87f, 0.5f);
        inputStyle.ContentMarginLeft = 20;
        inputStyle.ContentMarginRight = 20;
        inputStyle.ContentMarginTop = 12;
        inputStyle.ContentMarginBottom = 12;
        inputField.AddThemeStyleboxOverride("normal", inputStyle);
        inputField.AddThemeStyleboxOverride("focus", inputStyle);
        inputField.AddThemeStyleboxOverride("read_only", inputStyle);
        inputField.AddThemeFontSizeOverride("font_size", 26);
        inputField.AddThemeFontSizeOverride("font_placeholder_size", 26);
        inputField.AddThemeColorOverride("font_color", TextColor);
        inputField.AddThemeColorOverride("font_placeholder_color", new Color(0.62f, 0.62f, 0.65f));
        inputField.AddThemeColorOverride("caret_color", TextColor);
        inputField.AddThemeColorOverride("selection_color", new Color(0.585f, 0.925f, 0.41f, 0.35f));
        inputField.TextSubmitted += _ => SendFromInput();
        // 只读会话里点输入框：不弹键盘，只轻轻提醒一句（第四章"手滑"用）
        inputField.GuiInput += ev =>
        {
            if (ActiveReadOnly && ev is InputEventMouseButton mb && mb.Pressed
                && !string.IsNullOrEmpty(currentChatData?.ReadOnlyToast))
                ShowToast(currentChatData.ReadOnlyToast);
        };
        footerRow.AddChild(inputField);

        // 发送按钮
        var sendBtn = new Button
        {
            Text = "发送",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(106, 52),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var sendNormal = new StyleBoxFlat { BgColor = MyBubbleColor };
        sendNormal.SetCornerRadiusAll(10);
        var sendHover = new StyleBoxFlat { BgColor = new Color(0.65f, 0.95f, 0.48f) };
        sendHover.SetCornerRadiusAll(10);
        var sendPressed = new StyleBoxFlat { BgColor = new Color(0.5f, 0.82f, 0.35f) };
        sendPressed.SetCornerRadiusAll(10);
        sendBtn.AddThemeStyleboxOverride("normal", sendNormal);
        sendBtn.AddThemeStyleboxOverride("hover", sendHover);
        sendBtn.AddThemeStyleboxOverride("pressed", sendPressed);
        sendBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        sendBtn.AddThemeFontSizeOverride("font_size", 26);
        sendBtn.AddThemeColorOverride("font_color", new Color(0.05f, 0.3f, 0.06f));
        sendBtn.Pressed += SendFromInput;
        footerRow.AddChild(sendBtn);

        // ---- ＋ 功能面板（默认隐藏，弹出在输入栏上方）----
        plusPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var plusPanelStyle = new StyleBoxFlat { BgColor = new Color(0.955f, 0.955f, 0.965f) };
        plusPanelStyle.BorderWidthTop = 1;
        plusPanelStyle.BorderColor = new Color(0, 0, 0, 0.07f);
        plusPanel.AddThemeStyleboxOverride("panel", plusPanelStyle);
        plusPanel.AnchorTop = 1;
        plusPanel.AnchorRight = 1;
        plusPanel.AnchorBottom = 1;
        plusPanel.OffsetTop = -102 - 190;
        plusPanel.OffsetBottom = -102;
        plusPanel.GrowHorizontal = GrowDirection.Both;
        plusPanel.GrowVertical = GrowDirection.Begin;
        plusPanel.Visible = false;
        chatPage.AddChild(plusPanel);

        var plusGrid = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plusGrid.AddThemeConstantOverride("separation", 26);
        plusPanel.AddChild(UiKit.WrapMargin(plusGrid, 36, 40, 36, 40));
        AddPlusItem(plusGrid, "相册");
        AddPlusItem(plusGrid, "拍摄");
        AddPlusItem(plusGrid, "位置");
        AddPlusItem(plusGrid, "红包");

        // ---- 预设回复条（默认隐藏，浮在输入栏上方）----
        quickBar = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        quickBar.AddThemeConstantOverride("separation", 12);
        quickBar.AnchorTop = 1;
        quickBar.AnchorRight = 1;
        quickBar.AnchorBottom = 1;
        quickBar.OffsetLeft = 24;
        quickBar.OffsetTop = -102 - 300;
        quickBar.OffsetRight = -24;
        quickBar.OffsetBottom = -114;
        quickBar.GrowHorizontal = GrowDirection.Both;
        quickBar.GrowVertical = GrowDirection.Begin;
        quickBar.Visible = false;
        chatPage.AddChild(quickBar);

        // ---- "对方正在输入…"指示（默认隐藏，浮在输入栏上方靠左）----
        typingChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var typingStyle = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.95f) };
        typingStyle.SetCornerRadiusAll(16);
        typingStyle.ContentMarginLeft = 18;
        typingStyle.ContentMarginRight = 18;
        typingStyle.ContentMarginTop = 10;
        typingStyle.ContentMarginBottom = 10;
        typingChip.AddThemeStyleboxOverride("panel", typingStyle);
        typingChip.AnchorTop = 1;
        typingChip.AnchorBottom = 1;
        typingChip.OffsetLeft = 26;
        typingChip.OffsetTop = -106 - 50;
        typingChip.OffsetBottom = -106;
        typingChip.GrowVertical = GrowDirection.Begin;
        typingChip.Visible = false;
        chatPage.AddChild(typingChip);
        var typingLabel = new Label { Text = "对方正在输入…", MouseFilter = MouseFilterEnum.Ignore };
        typingLabel.AddThemeFontSizeOverride("font_size", 22);
        typingLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        typingChip.AddChild(typingLabel);

        // ---- "往上滑"提示 ----
        hintChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var hintStyle = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.88f) };
        hintStyle.SetCornerRadiusAll(14);
        hintStyle.ContentMarginLeft = 16;
        hintStyle.ContentMarginRight = 16;
        hintStyle.ContentMarginTop = 8;
        hintStyle.ContentMarginBottom = 8;
        hintChip.AddThemeStyleboxOverride("panel", hintStyle);
        hintChip.AnchorLeft = 0.5f;
        hintChip.AnchorRight = 0.5f;
        hintChip.OffsetLeft = -175;
        hintChip.OffsetTop = 116;
        hintChip.OffsetRight = 175;
        hintChip.OffsetBottom = 162;
        hintChip.GrowHorizontal = GrowDirection.Both;
        chatPage.AddChild(hintChip);
        var hintLabel = new Label
        {
            Text = "往上滑，看看之前的聊天",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hintLabel.AddThemeFontSizeOverride("font_size", 22);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.42f, 0.45f));
        hintChip.AddChild(hintLabel);

        // ---- ··· 下拉菜单（默认隐藏）----
        menuCatcher = new ColorRect { Color = new Color(0, 0, 0, 0f), MouseFilter = MouseFilterEnum.Stop };
        menuCatcher.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        menuCatcher.GuiInput += e => { if (e is InputEventMouseButton m && m.Pressed) HideMenu(); };
        menuCatcher.Visible = false;
        chatPage.AddChild(menuCatcher);

        menuPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var menuStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.29f, 0.29f, 0.31f, 0.96f),
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
        };
        menuStyle.ShadowColor = new Color(0, 0, 0, 0.3f);
        menuStyle.ShadowSize = 10;
        menuStyle.ShadowOffset = new Vector2(0, 3);
        menuPanel.AddThemeStyleboxOverride("panel", menuStyle);
        menuPanel.AnchorLeft = 1;
        menuPanel.AnchorRight = 1;
        menuPanel.OffsetLeft = -252;
        menuPanel.OffsetTop = 108;
        menuPanel.OffsetRight = -18;
        menuPanel.OffsetBottom = 108 + 3 * 78 + 12;
        menuPanel.GrowHorizontal = GrowDirection.Begin;
        menuPanel.Visible = false;
        chatPage.AddChild(menuPanel);

        var menuBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        menuBox.AddThemeConstantOverride("separation", 0);
        menuPanel.AddChild(UiKit.WrapMargin(menuBox, 0, 6, 0, 6));

        AddMenuItem(menuBox, "发起通话", () =>
        {
            HideMenu();
            ShowToast("嘟——嘟——\n对方没有接\n（这个小功能还是摆设～）");
        });
        AddMenuItem(menuBox, "清空聊天记录", () =>
        {
            HideMenu();
            clearedChats.Add(currentContact.Id);
            RebuildChatRows();
            ShowToast("聊天记录已清空");
        });
        AddMenuItem(menuBox, "收起手机", () =>
        {
            HideMenu();
            Close();
        });
    }

    /// <summary>顶栏按钮（返回 / ···）：透明底、可点</summary>
    private static Button MakeHeaderButton(string text, int fontSize)
    {
        var btn = new Button
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.AddThemeColorOverride("font_color", HeaderTextColor);
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
        return btn;
    }

    /// <summary>下拉菜单里的一行</summary>
    private static void AddMenuItem(VBoxContainer box, string text, Action onClick)
    {
        if (box.GetChildCount() > 0)
        {
            var line = new ColorRect { Color = new Color(1, 1, 1, 0.12f), MouseFilter = MouseFilterEnum.Ignore };
            line.CustomMinimumSize = new Vector2(0, 1);
            box.AddChild(line);
        }

        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 78),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 28);
        btn.AddThemeColorOverride("font_color", Colors.White);
        var empty = new StyleBoxEmpty();
        var pressed = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.12f) };
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.Pressed += onClick;
        box.AddChild(btn);
    }

    private void ToggleMenu()
    {
        bool show = !menuPanel.Visible;
        menuPanel.Visible = show;
        menuCatcher.Visible = show;
    }

    private void HideMenu()
    {
        menuPanel.Visible = false;
        menuCatcher.Visible = false;
    }

    // ==================== 搭建：主框架 / 朋友圈 / toast ====================

    private void BuildMainPage(ContactsData contacts)
    {
        mainPage = new WeChatMainPage();
        mainPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.AddChild(mainPage);
        mainPage.Build(contacts);

        mainPage.ContactSelected += id =>
        {
            var c = contacts.Contacts.Find(x => x.Id == id);
            if (c != null) ShowChat(c);
        };
        mainPage.PageOpened += key =>
        {
            if (key == "subscriptions") SetPage(subscriptionsPage);
            else if (key == "steps") SetPage(stepsPage);
        };
        mainPage.MomentsOpened += () => SetPage(momentsPage);
        mainPage.ToastRequested += ShowToast;
        mainPage.Visible = false;
    }

    private void BuildMomentsPage()
    {
        momentsPage = new MomentsPage();
        momentsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.AddChild(momentsPage);
        momentsPage.Build(WeChatData.LoadMoments());
        momentsPage.BackPressed += ShowMain;
        momentsPage.Visible = false;
    }

    /// <summary>订阅号消息 / 微信运动两个可点开的系统页（第九轮）</summary>
    private void BuildSystemPages()
    {
        var data = WeChatData.LoadSystem();

        subscriptionsPage = new SubscriptionsPage();
        subscriptionsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.AddChild(subscriptionsPage);
        subscriptionsPage.Build(data);
        subscriptionsPage.BackPressed += ShowMain;
        subscriptionsPage.ToastRequested += ShowToast;
        subscriptionsPage.Visible = false;

        stepsPage = new StepsRankingPage();
        stepsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pageHost.AddChild(stepsPage);
        stepsPage.Build(data);
        stepsPage.BackPressed += ShowMain;
        stepsPage.ToastRequested += ShowToast;
        stepsPage.Visible = false;
    }

    private void BuildToast()
    {
        toastChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.09f, 0.82f) };
        style.SetCornerRadiusAll(12);
        style.ContentMarginLeft = 28;
        style.ContentMarginRight = 28;
        style.ContentMarginTop = 16;
        style.ContentMarginBottom = 16;
        toastChip.AddThemeStyleboxOverride("panel", style);
        toastChip.AnchorLeft = 0.5f;
        toastChip.AnchorTop = 0.5f;
        toastChip.AnchorRight = 0.5f;
        toastChip.AnchorBottom = 0.5f;
        toastChip.OffsetLeft = -220;
        toastChip.OffsetTop = -70;
        toastChip.OffsetRight = 220;
        toastChip.OffsetBottom = 70;
        toastChip.GrowHorizontal = GrowDirection.Both;
        toastChip.GrowVertical = GrowDirection.Both;
        toastChip.Visible = false;
        pageHost.AddChild(toastChip);

        toastLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        toastLabel.AddThemeFontSizeOverride("font_size", 24);
        toastLabel.AddThemeColorOverride("font_color", Colors.White);
        toastChip.AddChild(toastLabel);
    }

    /// <summary>居中小提示：淡入 → 停 1.8 秒 → 淡出</summary>
    private void ShowToast(string text)
    {
        toastLabel.Text = text;
        toastChip.Visible = true;
        toastChip.Modulate = new Color(1, 1, 1, 0);

        toastTween?.Kill();
        toastTween = CreateTween();
        toastTween.TweenProperty(toastChip, "modulate:a", 1f, 0.15);
        toastTween.TweenInterval(1.8);
        toastTween.TweenProperty(toastChip, "modulate:a", 0f, 0.3);
        toastTween.TweenCallback(Callable.From(() => toastChip.Visible = false));
    }

    // ==================== 动画 / 输入 ====================

    /// <summary>入场动画（等一帧布局完成后再滑，避免位置跳动）</summary>
    private async void PlayEnterAnimation()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree() || closing)
            return;

        var targetPos = phonePanel.Position;
        phonePanel.Position = targetPos + new Vector2(0, 60);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "modulate:a", 1f, 0.25);
        tween.TweenProperty(phonePanel, "position", targetPos, 0.4)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>滚到底部（要等两帧：容器排完版滚动条才知道有多长）</summary>
    private async void ScrollToBottom()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree() || closing)
            return;
        scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
    }

    // ==================== 幽灵打字（第四章"手滑"） ====================

    /// <summary>已经表演过幽灵打字的会话（每个会话只演一次）</summary>
    private static readonly HashSet<string> ghostPlayedChats = new();

    /// <summary>
    /// 幽灵打字：输入框自己一个字一个字打出「最近还好吗」，
    /// 停一会儿，再一个字一个字删掉——像玩家的手自己动了。
    /// 只在带 ghostTyping 数据的会话里演，且每个会话只演一次。
    /// </summary>
    private void MaybePlayGhostTyping()
    {
        var g = currentChatData?.GhostTyping;
        if (g == null || string.IsNullOrEmpty(g.Text) || ghostPlayedChats.Contains(currentContact.Id))
            return;
        ghostPlayedChats.Add(currentContact.Id);

        string cid = currentContact.Id;
        string full = g.Text;
        ghostTween?.Kill();
        var tw = CreateTween();
        ghostTween = tw;

        tw.TweenInterval(g.StartDelay); // 先让玩家自己安静看一会儿聊天记录
        string shown = "";
        foreach (char ch in full)
        {
            shown += ch;
            string snapshot = shown;
            tw.TweenCallback(Callable.From(() =>
            {
                if (currentContact?.Id == cid && IsInsideTree())
                    inputField.Text = snapshot;
            }));
            tw.TweenInterval(g.TypeSpeed);
        }
        tw.TweenInterval(g.Hold);
        for (int len = full.Length - 1; len >= 0; len--)
        {
            string snapshot = full.Substring(0, len);
            tw.TweenCallback(Callable.From(() =>
            {
                if (currentContact?.Id == cid && IsInsideTree())
                    inputField.Text = snapshot;
            }));
            tw.TweenInterval(g.DeleteSpeed);
        }
        if (!string.IsNullOrEmpty(g.AfterToast))
        {
            tw.TweenCallback(Callable.From(() =>
            {
                if (currentContact?.Id == cid && IsInsideTree())
                    ShowToast(g.AfterToast);
            }));
        }
    }

    /// <summary>
    /// 手机框内底部横条：home 指示条 + "放下手机"按钮。
    /// 收起入口放进手机框里，手机外面的世界完全不可交互（第六轮反馈）。
    /// </summary>
    private void BuildBottomBar()
    {
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.90f, 0.915f),
            CornerRadiusBottomLeft = 28,
            CornerRadiusBottomRight = 28,
        };
        style.BorderWidthTop = 1;
        style.BorderColor = new Color(0, 0, 0, 0.06f);
        bar.AddThemeStyleboxOverride("panel", style);
        bar.AnchorTop = 1;
        bar.AnchorRight = 1;
        bar.AnchorBottom = 1;
        bar.OffsetTop = -56;
        bar.GrowHorizontal = GrowDirection.Both;
        bar.GrowVertical = GrowDirection.Begin;
        phonePanel.AddChild(bar);

        var box = new Control { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bar.AddChild(box);

        // home 指示条（真手机底部的那根小横线）
        var home = new ColorRect { Color = new Color(0, 0, 0, 0.18f), MouseFilter = MouseFilterEnum.Ignore };
        home.AnchorLeft = 0.5f;
        home.AnchorRight = 0.5f;
        home.OffsetLeft = -60;
        home.OffsetRight = 60;
        home.OffsetTop = 7;
        home.OffsetBottom = 11;
        home.GrowHorizontal = GrowDirection.Both;
        box.AddChild(home);

        var closeBtn = new Button
        {
            Text = "放下手机",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 22);
        closeBtn.AddThemeColorOverride("font_color", new Color(0.38f, 0.38f, 0.42f));
        var hover = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.06f) };
        hover.SetCornerRadiusAll(12);
        var pressed = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.12f) };
        pressed.SetCornerRadiusAll(12);
        closeBtn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        closeBtn.AddThemeStyleboxOverride("hover", hover);
        closeBtn.AddThemeStyleboxOverride("pressed", pressed);
        closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        closeBtn.AnchorLeft = 0.5f;
        closeBtn.AnchorRight = 0.5f;
        closeBtn.OffsetLeft = -90;
        closeBtn.OffsetRight = 90;
        closeBtn.OffsetTop = 15;
        closeBtn.OffsetBottom = 50;
        closeBtn.GrowHorizontal = GrowDirection.Both;
        closeBtn.Pressed += Close;
        box.AddChild(closeBtn);
    }

    /// <summary>＋ 面板里的一格（演示版：点了弹提示）</summary>
    private void AddPlusItem(HBoxContainer grid, string name)
    {
        var btn = new Button
        {
            Text = name,
            CustomMinimumSize = new Vector2(112, 100),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var normal = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.95f) };
        normal.SetCornerRadiusAll(12);
        var hover = new StyleBoxFlat { BgColor = new Color(0.93f, 0.93f, 0.94f) };
        hover.SetCornerRadiusAll(12);
        var pressed = new StyleBoxFlat { BgColor = new Color(0.87f, 0.87f, 0.88f) };
        pressed.SetCornerRadiusAll(12);
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.AddThemeFontSizeOverride("font_size", 26);
        btn.AddThemeColorOverride("font_color", new Color(0.32f, 0.32f, 0.36f));
        btn.Pressed += () => ShowToast($"演示版：「{name}」暂未开放");
        grid.AddChild(btn);
    }

    private void TogglePlusPanel()
    {
        if (ActiveReadOnly)
        {
            ShowToast("只读会话，发不了这些～");
            return;
        }
        plusPanel.Visible = !plusPanel.Visible;
    }

    private void HidePlusPanel()
    {
        if (plusPanel != null)
            plusPanel.Visible = false;
    }

    /// <summary>
    /// 只读会话（第九轮 / 第十轮扩展）：实验群围观、文件传输助手、第四章的旧聊天——
    /// 输入框禁言，发送和＋面板改成 toast，聊天记录保持纯观赏。
    /// 第十轮起剧本级 readOnly 时，占位文字和点按 toast 都可以从 JSON 取。
    /// </summary>
    private void ApplyReadOnlyMode(ContactData contact)
    {
        bool readOnly = ActiveReadOnly;
        inputField.Editable = !readOnly;
        if (!readOnly)
        {
            inputField.PlaceholderText = "发消息……";
            return;
        }
        if (contact.Kind == "group")
            inputField.PlaceholderText = "群聊围观中，不参与发言";
        else if (!string.IsNullOrEmpty(currentChatData?.ReadOnlyHint))
            inputField.PlaceholderText = currentChatData.ReadOnlyHint;
        else
            inputField.PlaceholderText = "只读消息，无法回复";
    }

    /// <summary>输入框/发送按钮：把文字作为我的气泡发出去（自由输入不影响数值）</summary>
    private void SendFromInput()
    {
        if (ActiveReadOnly)
        {
            if (currentContact.Kind == "group")
                ShowToast("群里导师随时盯着，谁也不敢接话～");
            else if (!string.IsNullOrEmpty(currentChatData?.ReadOnlyToast))
                ShowToast(currentChatData.ReadOnlyToast);
            else
                ShowToast("这里只能看，不能发言～");
            return;
        }

        string text = inputField.Text.Trim();
        if (text.Length == 0)
        {
            ShowToast("先输入一点内容再发送～");
            return;
        }

        inputField.Text = "";
        HidePlusPanel();
        AppendMessage(new ChatMessageData { Sender = "me", Text = text });
        SchedulePersonaReply(currentContact.Id, text);
    }

    // ==================== 人设回复引擎 ====================

    /// <summary>"对方正在输入…"指示</summary>
    private void ShowTypingChip(string contactId)
    {
        if (currentContact?.Id == contactId && !closing && quickBar is { Visible: false })
            typingChip.Visible = true;
    }

    private void HideTypingChip()
    {
        if (typingChip != null)
            typingChip.Visible = false;
    }

    /// <summary>
    /// 人设回复引擎入口：把玩家消息交给 ReplyEngine 按关键词归类挑话，
    /// 按人设延迟回复（金艮秒回、导师隔半天），回复前亮出"对方正在输入…"。
    /// 手机就算已经放下，回复也会先落到会话数据里，下次打开照还在。
    /// 回复不影响好感/勇气——数值仍由剧情选择控制，防止刷分。
    /// </summary>
    private void SchedulePersonaReply(string contactId, string text)
    {
        if (!ReplyEngine.HasProfile(contactId))
            return;
        var decision = ReplyEngine.OnPlayerMessage(contactId, text);
        if (decision == null)
            return;

        float delay = Mathf.Max(0.6f, decision.Delay);

        // 回复前先亮"对方正在输入…"；回复慢的人设只在最后 3 秒亮，更像真的在打字
        float typingAt = delay > 4.5f ? delay - 3.0f : 0.2f;
        GetTree().CreateTimer(typingAt).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(this))
                ShowTypingChip(contactId);
        };

        GetTree().CreateTimer(delay).Timeout += () =>
        {
            var msg = new ChatMessageData { Sender = "other", Text = decision.Line };
            StoreIncoming(contactId, msg); // 数据先落库（static），手机关了也不丢

            if (!GodotObject.IsInstanceValid(this))
                return;
            HideTypingChip();
            if (currentContact != null && currentContact.Id == contactId && !closing)
            {
                rows.AddChild(MakeMessageRow(msg));
                ScrollToBottom();
            }
        };
    }

    /// <summary>往会话记录里补一条消息（只动 static 数据；UI 由调用方按需刷新）</summary>
    private static void StoreIncoming(string contactId, ChatMessageData msg)
    {
        if (!extraMessages.TryGetValue(contactId, out var list))
        {
            list = new List<ChatMessageData>();
            extraMessages[contactId] = list;
        }
        list.Add(msg);
    }

    /// <summary>往当前会话追加一条消息（本局内重开手机也还在）</summary>
    private void AppendMessage(ChatMessageData msg)
    {
        if (!extraMessages.TryGetValue(currentContact.Id, out var list))
        {
            list = new List<ChatMessageData>();
            extraMessages[currentContact.Id] = list;
        }
        list.Add(msg);

        rows.AddChild(MakeMessageRow(msg));
        ScrollToBottom();
    }

    /// <summary>预设回复条：标题 + 若干候选回复（选哪条会影响好感/勇气 → 影响结局）</summary>
    private void BuildQuickBar(List<QuickReplyData> replies)
    {
        foreach (var child in quickBar.GetChildren())
            child.QueueFree();

        var title = new Label
        {
            Text = "怎么回？",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        quickBar.AddChild(title);

        foreach (var q in replies)
        {
            var chip = new Button
            {
                Text = q.Text,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, // 整条宽，单行居中（不开自动换行，避免被压成竖条）
                CustomMinimumSize = new Vector2(0, 56),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            var normal = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.97f) };
            normal.SetCornerRadiusAll(18);
            normal.SetBorderWidthAll(1);
            normal.BorderColor = new Color(0.34f, 0.42f, 0.58f, 0.35f);
            normal.ContentMarginLeft = 24;
            normal.ContentMarginRight = 24;
            normal.ContentMarginTop = 12;
            normal.ContentMarginBottom = 12;
            normal.ShadowColor = new Color(0, 0, 0, 0.12f);
            normal.ShadowSize = 6;
            normal.ShadowOffset = new Vector2(0, 2);
            var hover = (StyleBoxFlat)normal.Duplicate();
            hover.BgColor = new Color(0.93f, 0.95f, 0.99f);
            var pressedSb = (StyleBoxFlat)normal.Duplicate();
            pressedSb.BgColor = new Color(0.87f, 0.9f, 0.96f);
            chip.AddThemeStyleboxOverride("normal", normal);
            chip.AddThemeStyleboxOverride("hover", hover);
            chip.AddThemeStyleboxOverride("pressed", pressedSb);
            chip.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            chip.AddThemeFontSizeOverride("font_size", 24);
            chip.AddThemeColorOverride("font_color", new Color(0.34f, 0.42f, 0.58f));
            chip.Pressed += () => OnQuickReply(q);
            quickBar.AddChild(chip);
        }

        quickBar.Visible = true;
    }

    private void HideQuickBar()
    {
        if (quickBar == null)
            return;
        quickBar.Visible = false;
        foreach (var child in quickBar.GetChildren())
            child.QueueFree();
    }

    /// <summary>
    /// 选了某条预设回复：发出去 → 加数值（飘字）→ 她隔一小会儿回一句。
    /// 数值进 GameManager，结局判定（好感/勇气档位）就会因此不同。
    /// </summary>
    private void OnQuickReply(QuickReplyData q)
    {
        usedQuickReplies.Add(currentContact.Id);
        HideQuickBar();
        HidePlusPanel();

        AppendMessage(new ChatMessageData { Sender = "me", Text = q.Text });

        if (q.Affection != 0) GameManager.Instance.AddAffection(q.Affection);
        if (q.Courage != 0) GameManager.Instance.AddCourage(q.Courage);
        if (q.Affection != 0 || q.Courage != 0)
            DialogueManager.Instance?.ShowStatToast(q.Affection, q.Courage);

        if (!string.IsNullOrEmpty(q.Reply))
        {
            GetTree().CreateTimer(0.9).Timeout += () =>
            {
                var msg = new ChatMessageData { Sender = "other", Text = q.Reply };
                StoreIncoming(currentContact.Id, msg); // 就算中途放下手机，回复也不丢
                if (!IsInsideTree() || closing || currentContact == null)
                    return;
                rows.AddChild(MakeMessageRow(msg));
                ScrollToBottom();
            };
        }
    }

    /// <summary>玩家一滑动 → 提示气泡淡出（只触发一次）</summary>
    private void OnScrolled(double value)
    {
        if (!hintArmed || closing || !hintChip.Visible)
            return;

        var tween = CreateTween();
        tween.TweenProperty(hintChip, "modulate:a", 0f, 0.4);
        tween.TweenCallback(Callable.From(() => hintChip.Visible = false));
    }

    // ==================== 消息行生成 ====================

    private Control MakeMessageRow(ChatMessageData msg)
    {
        if (msg.Type == "divider")
        {
            lastRowWasTimeInfo = true;
            return MakeDivider(msg.Text);
        }

        // 每 3 条消息插入一个时间戳（模拟微信的时间显示；上一行已是分割线/时间戳就不叠）
        if (messageIndex > 0 && messageIndex % 3 == 0 && !lastRowWasTimeInfo)
        {
            rows.AddChild(MakeTimestamp());
            lastRowWasTimeInfo = true;
        }
        else
        {
            lastRowWasTimeInfo = false;
        }
        messageIndex++;

        bool mine = msg.Sender == "me";
        bool isGroup = currentContact != null && currentContact.Kind == "group";

        var row = new HBoxContainer();
        row.Name = "Row";
        row.AddThemeConstantOverride("separation", 0); // 间距做进尾巴控件里，保证尾巴贴着气泡
        row.MouseFilter = MouseFilterEnum.Ignore;

        Control avatar = mine
            ? MakePhotoAvatar(WeChatData.MyAvatarPath, 84)
            : isGroup && !string.IsNullOrEmpty(msg.SenderName)
                ? MakeGroupMemberAvatar(msg.SenderName, 84)
                : MakeContactAvatar(currentContact, 84);

        Control bubble = msg.Type == "image"
            ? MakeImageBubble(msg.Image)
            : MakeTextBubble(msg.Text, mine);

        // 小尾巴：独立控件，紧贴气泡靠头像那一侧（不能放进气泡容器里，会被拉伸）
        var tail = new BubbleTail(mine ? MyBubbleColor : HerBubbleColor, mine);

        if (mine)
        {
            // 我：气泡靠右，尾巴在气泡右侧，头像最右
            row.AddChild(MakeHorizontalExpander());
            row.AddChild(bubble);
            row.AddChild(tail);
            row.AddChild(avatar);
        }
        else if (isGroup && !string.IsNullOrEmpty(msg.SenderName))
        {
            // 群聊：头像 + [昵称 / (尾巴+气泡)] 两列，昵称浮在气泡上方
            row.AddChild(avatar);
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            col.AddThemeConstantOverride("separation", 8);
            col.AddChild(MakeSenderName(msg.SenderName));
            var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.AddThemeConstantOverride("separation", 0);
            inner.AddChild(tail);
            inner.AddChild(bubble);
            col.AddChild(inner);
            row.AddChild(col);
            row.AddChild(MakeHorizontalExpander());
        }
        else
        {
            // 对方：头像最左，尾巴在气泡左侧
            row.AddChild(avatar);
            row.AddChild(tail);
            row.AddChild(bubble);
            row.AddChild(MakeHorizontalExpander());
        }
        return row;
    }

    /// <summary>群聊气泡上方的发言人昵称（微信式蓝灰小字）</summary>
    private static Control MakeSenderName(string name)
    {
        var label = new Label { Text = name, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(0.42f, 0.48f, 0.62f));
        var margin = UiKit.WrapMargin(label, 12, 0, 0, 0);
        return margin;
    }

    /// <summary>群成员头像：昵称能对上联系人就用 ta 的照片，否则首字色块</summary>
    private static Control MakeGroupMemberAvatar(string name, float size)
    {
        if (name == "我")
            return MakePhotoAvatar(WeChatData.MyAvatarPath, size);
        var member = WeChatData.FindByName(name);
        if (member != null)
            return MakeContactAvatar(member, size);
        var avatar = new InitialAvatar(name, size);
        avatar.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return avatar;
    }

    /// <summary>日期分割线（居中灰字）</summary>
    private static Control MakeDivider(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", DividerColor);
        return label;
    }

    /// <summary>微信风格时间戳（居中灰色小字，如 "晚上 9:32"）</summary>
    private static Control MakeTimestamp()
    {
        string[] times = { "晚上 9:20", "晚上 9:25", "晚上 9:31", "晚上 9:38", "晚上 9:45", "晚上 10:02", "晚上 10:15" };
        string time = times[GD.Randi() % times.Length];

        var label = new Label
        {
            Text = time,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", TimestampColor);
        return label;
    }

    /// <summary>文字气泡（不对称圆角 + 微阴影；尾巴是行内独立控件）</summary>
    private Control MakeTextBubble(string text, bool mine)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", TextColor);
        label.AddThemeConstantOverride("line_spacing", 6);

        // 量一下文字宽度，给气泡一个合适的"自然宽度"（超宽就换行）
        float textWidth = GetThemeDefaultFont()
            .GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
        label.CustomMinimumSize = new Vector2(Mathf.Min(textWidth + 4f, MaxTextBubbleWidth), 0);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        margin.AddChild(label);

        var bubble = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = mine ? MyBubbleColor : HerBubbleColor };

        // 微信风格不对称圆角：远离头像的三角大圆角(16)，靠近头像的角小圆角(4)
        style.SetCornerRadiusAll(16);
        if (mine)
            style.CornerRadiusTopRight = 4; // 我的气泡：右上角小（靠近头像）
        else
            style.CornerRadiusTopLeft = 4;  // 对方气泡：左上角小（靠近头像）

        // 微阴影
        style.ShadowColor = new Color(0, 0, 0, 0.08f);
        style.ShadowSize = 3;
        style.ShadowOffset = new Vector2(0, 1);

        bubble.AddThemeStyleboxOverride("panel", style);
        bubble.AddChild(margin);
        return bubble;
    }

    /// <summary>图片气泡（圆角、按原比例缩放）</summary>
    private Control MakeImageBubble(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        float height = ImageBubbleWidth * tex.GetHeight() / tex.GetWidth();

        var img = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(ImageBubbleWidth, height),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        img.Material = MakeMaskMaterial(ImageBubbleWidth, height, 12);
        return img;
    }

    /// <summary>照片头像（固定方形，圆角遮罩）</summary>
    private static Control MakePhotoAvatar(string path, float size)
    {
        var avatar = new TextureRect
        {
            Texture = GD.Load<Texture2D>(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = SizeFlags.ShrinkBegin, // 顶部对齐气泡
            MouseFilter = MouseFilterEnum.Ignore,
        };
        avatar.Material = MakeMaskMaterial(size, size, 12);
        return avatar;
    }

    /// <summary>对方头像：系统账号用图标块，联系人有图用图，没图用首字色块</summary>
    private static Control MakeContactAvatar(ContactData c, float size)
    {
        if (c != null && !string.IsNullOrEmpty(c.Icon))
            return WeChatMainPage.MakeListAvatar(c, size);
        if (c != null && !string.IsNullOrEmpty(c.Avatar) && ResourceLoader.Exists(c.Avatar))
            return MakePhotoAvatar(c.Avatar, size);
        var avatar = new InitialAvatar(c?.Name ?? "?", size);
        avatar.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return avatar;
    }

    /// <summary>撑满剩余空间的透明控件（把气泡推到左边或右边）</summary>
    private static Control MakeHorizontalExpander()
    {
        return new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    /// <summary>圆角遮罩材质（把方形图裁成圆角，见 rounded_mask.gdshader）</summary>
    public static ShaderMaterial MakeMaskMaterial(float width, float height, float radius)
    {
        roundedMaskShader ??= GD.Load<Shader>("res://assets/shaders/rounded_mask.gdshader");
        var material = new ShaderMaterial { Shader = roundedMaskShader };
        material.SetShaderParameter("rect_size", new Vector2(width, height));
        material.SetShaderParameter("radius", radius);
        return material;
    }

    // ==================== 数据加载 ====================

    /// <summary>读取 data/chat/{chatId}.json（和对话系统同一套 JSON 流派）</summary>
    public static ChatScriptData LoadChatData(string chatId)
    {
        string path = $"res://data/chat/{chatId}.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[聊天] 找不到聊天文件：{path}");
            return new ChatScriptData();
        }

        string json = FileAccess.GetFileAsString(path);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<ChatScriptData>(json, options);
            data?.EnsureInitialized();
            return data ?? new ChatScriptData();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[聊天] JSON 解析失败 {path}：{e.Message}");
            return new ChatScriptData();
        }
    }
}

// ==================== 聊天数据结构 ====================

/// <summary>一份聊天记录（对应一个 JSON 文件）</summary>
public class ChatScriptData
{
    public string Title { get; set; } = "聊天";
    public List<ChatMessageData> Messages { get; set; }

    /// <summary>预设回复候选（浮在输入栏上方；选哪条加不同数值 → 影响结局）</summary>
    public List<QuickReplyData> QuickReplies { get; set; }

    // ---------- 第四章：旧聊天的"只读 + 幽灵打字" ----------

    /// <summary>剧本级只读：输入框禁言（与联系人级 ReadOnly 任一成立即生效）</summary>
    public bool ReadOnly { get; set; }

    /// <summary>只读时输入框里的占位文字（留空 = 用默认的"只读消息，无法回复"）</summary>
    public string ReadOnlyHint { get; set; } = "";

    /// <summary>只读时点输入框弹的 toast（留空 = 不弹）</summary>
    public string ReadOnlyToast { get; set; } = "";

    /// <summary>幽灵打字：输入框自己打字又删掉（留空 = 不演）</summary>
    public GhostTypingData GhostTyping { get; set; }

    public void EnsureInitialized()
    {
        Messages ??= new();
    }
}

/// <summary>幽灵打字剧本：打出 text → 停 hold 秒 → 一个字一个字删掉 → 弹 afterToast</summary>
public class GhostTypingData
{
    public string Text { get; set; } = "";
    public float StartDelay { get; set; } = 1.8f;
    public float TypeSpeed { get; set; } = 0.16f;
    public float Hold { get; set; } = 1.2f;
    public float DeleteSpeed { get; set; } = 0.07f;
    public string AfterToast { get; set; } = "";
}

/// <summary>一条预设回复：文案 + 好感/勇气增量 + 她的回应</summary>
public class QuickReplyData
{
    public string Text { get; set; } = "";
    public int Affection { get; set; }
    public int Courage { get; set; }
    public string Reply { get; set; } = "";
}

/// <summary>一条聊天消息</summary>
public class ChatMessageData
{
    public string Sender { get; set; } = "other"; // other（对方）/ me（我）/ divider（分割线）
    public string Type { get; set; } = "text";    // text / image / divider
    public string Text { get; set; } = "";        // 文字内容（divider 用 text 存日期）
    public string Image { get; set; } = "";       // 图片路径（type = image 时用）

    /// <summary>群聊里的发言人昵称（非空 = 气泡上方显示昵称 + 用该联系人的头像）</summary>
    public string SenderName { get; set; } = "";
}

/// <summary>
/// 气泡小尾巴：气泡靠头像那一侧的小三角形。
///
/// 【重要】它必须是"行里的兄弟节点"，不能放进气泡容器——
/// PanelContainer 会把所有子节点拉伸到整个气泡大小，尾巴就会变成
/// 一个盖住文字的大三角（第四轮的红框 bug 就是这么来的）。
/// 控件宽 26 = 14（头像间距，透明）+ 12（三角形本体），行间距设为 0，
/// 这样三角形刚好贴着气泡边缘，头像间距也保持不变。
/// </summary>
public partial class BubbleTail : Control
{
    private const float Gap = 14f;      // 透明间距（顶替原来的行间距）
    private const float TailWidth = 12f;
    private const float TailTop = 14f;  // 三角形顶端 y（和气泡内边距对齐）
    private const float TailHeight = 12f;

    private readonly Color tailColor;
    private readonly bool onRight; // true = 尾巴在右边（我的消息）

    public BubbleTail(Color color, bool onRightSide)
    {
        tailColor = color;
        onRight = onRightSide;
        CustomMinimumSize = new Vector2(Gap + TailWidth, TailTop + TailHeight + 4);
        SizeFlagsVertical = SizeFlags.ShrinkBegin; // 贴在行顶部
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2[] points;
        if (onRight)
        {
            // 我的消息：三角形在控件左侧 12px，指向右（贴气泡右边缘外侧？不——贴气泡左？)
            // 行顺序是 [气泡][尾巴][头像]：三角形要靠控件左边缘贴住气泡，尖端朝右没意义，
            // 实际微信里我的气泡尾巴在气泡右侧朝右指。这里控件在气泡右边，
            // 所以三角形 = 靠左的 12px，尖端朝右外指会被头像间距吃掉——
            // 正确画法：底边贴控件左缘（= 气泡右边缘），尖端朝右。
            points = new Vector2[]
            {
                new Vector2(0, TailTop),
                new Vector2(TailWidth, TailTop + TailHeight * 0.45f),
                new Vector2(0, TailTop + TailHeight),
            };
        }
        else
        {
            // 对方消息：控件在气泡左边，透明间距在左、三角形靠右缘贴住气泡，尖端朝左
            points = new Vector2[]
            {
                new Vector2(Gap + TailWidth, TailTop),
                new Vector2(Gap, TailTop + TailHeight * 0.45f),
                new Vector2(Gap + TailWidth, TailTop + TailHeight),
            };
        }
        DrawColoredPolygon(points, tailColor);
    }
}
