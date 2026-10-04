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
    private Control chatPage;
    private WeChatMainPage mainPage;
    private MomentsPage momentsPage;

    // 聊天页节点
    private ScrollContainer scroll;
    private VBoxContainer rows;
    private Control hintChip;
    private Label nameLabel;
    private Control menuCatcher;
    private PanelContainer menuPanel;

    // toast
    private PanelContainer toastChip;
    private Label toastLabel;
    private Tween toastTween;

    private ContactData currentContact;
    private bool closing;    // 正在收起（防止重复触发）
    private bool hintArmed;  // "滑一滑"提示是否已武装（入场滚动不算）
    private int messageIndex; // 消息序号（用于生成时间戳）
    private bool lastRowWasTimeInfo; // 上一行是分割线/时间戳 → 不再叠时间戳

    public override void _Ready()
    {
        phonePanel = GetNode<Panel>("PhonePanel");

        // 点黑幕 = 收起手机
        GetNode<ColorRect>("Backdrop").GuiInput += OnBackdropInput;

        // "收起手机"按钮
        GetNode<Button>("CloseChip").Pressed += Close;
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
        BuildToast();

        // 页面搭好之后再统一接按钮音效
        UiSounds.WireAll(this);

        // 找到 chatId 对应的联系人（按 ChatFile 或 id 匹配）
        var contact = contacts.Contacts.Find(c => c.ChatFile == chatId || c.Id == chatId)
                      ?? new ContactData
                      {
                          Id = chatId,
                          Name = "同桌",
                          Avatar = "res://assets/art/chat/her_avatar_v1.png",
                          ChatFile = chatId,
                      };

        ShowChat(contact);

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
    }

    /// <summary>打开某位联系人的聊天</summary>
    private void ShowChat(ContactData contact)
    {
        currentContact = contact;
        nameLabel.Text = contact.Name;
        HideMenu();
        RebuildChatRows();
        SetPage(chatPage);
        ScrollToBottom();

        // 1.2 秒后武装提示监听（避开入场自动滚动）
        hintArmed = false;
        hintChip.Visible = true;
        hintChip.Modulate = Colors.White;
        GetTree().CreateTimer(1.2).Timeout += () => hintArmed = true;
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

        if (clearedChats.Contains(currentContact.Id))
        {
            rows.AddChild(MakeDivider("聊天记录已清空"));
            return;
        }

        var messages = !string.IsNullOrEmpty(currentContact.ChatFile)
            ? LoadChatData(currentContact.ChatFile).Messages
            : currentContact.Messages;

        foreach (var msg in messages)
            rows.AddChild(MakeMessageRow(msg));
    }

    // ==================== 搭建：聊天页 ====================

    private void BuildChatPage()
    {
        chatPage = new Control { MouseFilter = MouseFilterEnum.Stop };
        chatPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        phonePanel.AddChild(chatPage);

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
        pad.AddThemeConstantOverride("margin_top", 20);
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
            CornerRadiusBottomLeft = 28,
            CornerRadiusBottomRight = 28,
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

        // ＋ 圆圈
        var plusCircle = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var plusStyle = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.92f) };
        plusStyle.SetCornerRadiusAll(32);
        plusCircle.AddThemeStyleboxOverride("panel", plusStyle);
        plusCircle.CustomMinimumSize = new Vector2(64, 64);
        footerRow.AddChild(plusCircle);
        var plusLabel = new Label
        {
            Text = "＋",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        plusLabel.AddThemeFontSizeOverride("font_size", 34);
        plusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.33f));
        plusCircle.AddChild(plusLabel);

        // 输入框
        var inputField = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var inputStyle = new StyleBoxFlat { BgColor = new Color(0.96f, 0.96f, 0.97f) };
        inputStyle.SetCornerRadiusAll(10);
        inputStyle.SetBorderWidthAll(1);
        inputStyle.BorderColor = new Color(0.85f, 0.85f, 0.87f, 0.5f);
        inputStyle.ContentMarginLeft = 20;
        inputField.AddThemeStyleboxOverride("panel", inputStyle);
        inputField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footerRow.AddChild(inputField);
        var placeholder = new Label
        {
            Text = "发消息……",
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        placeholder.AddThemeFontSizeOverride("font_size", 26);
        placeholder.AddThemeColorOverride("font_color", new Color(0.62f, 0.62f, 0.65f));
        inputField.AddChild(placeholder);

        // 发送按钮
        var sendChip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var sendStyle = new StyleBoxFlat { BgColor = MyBubbleColor };
        sendStyle.SetCornerRadiusAll(10);
        sendChip.AddThemeStyleboxOverride("panel", sendStyle);
        sendChip.CustomMinimumSize = new Vector2(106, 52);
        sendChip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        footerRow.AddChild(sendChip);
        var sendLabel = new Label
        {
            Text = "发送",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        sendLabel.AddThemeFontSizeOverride("font_size", 26);
        sendLabel.AddThemeColorOverride("font_color", new Color(0.05f, 0.3f, 0.06f));
        sendChip.AddChild(sendLabel);

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
        phonePanel.AddChild(mainPage);
        mainPage.Build(contacts);

        mainPage.ContactSelected += id =>
        {
            var c = contacts.Contacts.Find(x => x.Id == id);
            if (c != null) ShowChat(c);
        };
        mainPage.MomentsOpened += () => SetPage(momentsPage);
        mainPage.ToastRequested += ShowToast;
        mainPage.Visible = false;
    }

    private void BuildMomentsPage()
    {
        momentsPage = new MomentsPage();
        momentsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        phonePanel.AddChild(momentsPage);
        momentsPage.Build(WeChatData.LoadMoments());
        momentsPage.BackPressed += ShowMain;
        momentsPage.Visible = false;
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
        phonePanel.AddChild(toastChip);

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

    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            Close();
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

        var row = new HBoxContainer();
        row.Name = "Row";
        row.AddThemeConstantOverride("separation", 0); // 间距做进尾巴控件里，保证尾巴贴着气泡
        row.MouseFilter = MouseFilterEnum.Ignore;

        Control avatar = mine
            ? MakePhotoAvatar("res://assets/art/chat/mc_avatar_v1.png", 84)
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

    /// <summary>对方头像：联系人有图用图，没图用首字色块</summary>
    private static Control MakeContactAvatar(ContactData c, float size)
    {
        if (!string.IsNullOrEmpty(c.Avatar) && ResourceLoader.Exists(c.Avatar))
            return MakePhotoAvatar(c.Avatar, size);
        var avatar = new InitialAvatar(c.Name, size);
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

    public void EnsureInitialized()
    {
        Messages ??= new();
    }
}

/// <summary>一条聊天消息</summary>
public class ChatMessageData
{
    public string Sender { get; set; } = "other"; // other（对方）/ me（我）/ divider（分割线）
    public string Type { get; set; } = "text";    // text / image / divider
    public string Text { get; set; } = "";        // 文字内容（divider 用 text 存日期）
    public string Image { get; set; } = "";       // 图片路径（type = image 时用）
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
