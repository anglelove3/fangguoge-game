using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 微信主框架页：底部四标签（微信 / 通讯录 / 发现 / 我）
///
///   微信   → 会话列表（13 位联系人，头像 + 最后一条消息预览）
///   通讯录 → 联系人列表
///   发现   → 朋友圈入口（带红点）+ 视频号/游戏（占位演出）
///   我     → 自己的资料卡 + 占位条目
///
/// 全部代码搭建，不占场景节点。点击联系人会抛 ContactSelected(id)，
/// 由 ChatOverlay 负责打开对应的聊天页。
/// </summary>
public partial class WeChatMainPage : Control
{
    /// <summary>点了某位联系人（参数 = 联系人 id）</summary>
    public event Action<string> ContactSelected;
    /// <summary>点了"朋友圈"</summary>
    public event Action MomentsOpened;
    /// <summary>点了占位条目（参数 = 提示文案）</summary>
    public event Action<string> ToastRequested;

    private static readonly Color PageBg = new Color(0.935f, 0.935f, 0.94f);
    private static readonly Color TextDark = new Color(0.11f, 0.11f, 0.13f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color TabOn = new Color(0.16f, 0.62f, 0.30f);
    private static readonly Color TabOff = new Color(0.45f, 0.45f, 0.48f);

    private const float HeaderHeight = 96f;
    private const float TabBarHeight = 100f;

    private Label titleLabel;
    private readonly Control[] pages = new Control[4];
    private readonly TabIcon[] tabIcons = new TabIcon[4];
    private readonly Label[] tabLabels = new Label[4];
    private int currentTab;

    // ==================== 搭建 ====================

    public void Build(ContactsData contacts)
    {
        MouseFilter = MouseFilterEnum.Stop;

        // ---- 顶栏 ----
        var header = new Control { MouseFilter = MouseFilterEnum.Ignore };
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        header.OffsetBottom = HeaderHeight;
        AddChild(header);

        titleLabel = new Label
        {
            Text = "微信",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 32);
        titleLabel.AddThemeColorOverride("font_color", TextDark);
        titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChild(titleLabel);

        var plusBtn = MakeFlatButton("＋", 40);
        plusBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        plusBtn.OffsetLeft = -84;
        plusBtn.OffsetTop = 18;
        plusBtn.OffsetRight = -24;
        plusBtn.OffsetBottom = 78;
        plusBtn.Pressed += () => ToastRequested?.Invoke("这个＋号目前只是摆设～");
        header.AddChild(plusBtn);

        // ---- 内容区 ----
        var content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        content.AnchorTop = 0;
        content.OffsetTop = HeaderHeight;
        content.AnchorRight = 1;
        content.AnchorBottom = 1;
        content.OffsetBottom = -TabBarHeight;
        content.GrowHorizontal = GrowDirection.Both;
        content.GrowVertical = GrowDirection.Both;
        AddChild(content);

        pages[0] = BuildSessionPage(contacts);
        pages[1] = BuildContactsPage(contacts);
        pages[2] = BuildDiscoverPage();
        pages[3] = BuildMePage();
        foreach (var page in pages)
        {
            page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.AddChild(page);
        }

        // ---- 底部标签栏 ----
        var tabBar = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var barStyle = new StyleBoxFlat { BgColor = new Color(0.87f, 0.87f, 0.88f) };
        barStyle.SetBorderWidthAll(0);
        barStyle.BorderWidthTop = 1;
        barStyle.BorderColor = new Color(0, 0, 0, 0.08f);
        tabBar.AddThemeStyleboxOverride("panel", barStyle);
        tabBar.AnchorTop = 1;
        tabBar.AnchorRight = 1;
        tabBar.AnchorBottom = 1;
        tabBar.OffsetTop = -TabBarHeight;
        tabBar.GrowHorizontal = GrowDirection.Both;
        tabBar.GrowVertical = GrowDirection.Begin;
        AddChild(tabBar);

        var tabBox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tabBox.AddThemeConstantOverride("separation", 0);
        tabBar.AddChild(tabBox);

        string[] names = { "微信", "通讯录", "发现", "我" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var tab = new Button { MouseFilter = MouseFilterEnum.Stop };
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            ApplyEmptyStyle(tab);

            var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            vbox.AddThemeConstantOverride("separation", 4);
            vbox.Alignment = BoxContainer.AlignmentMode.Center;
            tab.AddChild(vbox);
            vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // Button 不是容器，子节点要手动铺满

            var icon = new TabIcon((TabIcon.Kind)idx) { MouseFilter = MouseFilterEnum.Ignore };
            icon.CustomMinimumSize = new Vector2(44, 44);
            icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            vbox.AddChild(icon);
            tabIcons[i] = icon;

            var label = new Label
            {
                Text = names[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 20);
            vbox.AddChild(label);
            tabLabels[i] = label;

            tab.Pressed += () => SetTab(idx);
            tabBox.AddChild(tab);
        }

        SetTab(0);
    }

    /// <summary>切换底部标签</summary>
    public void SetTab(int idx)
    {
        currentTab = idx;
        string[] titles = { "微信", "通讯录", "发现", "我" };
        titleLabel.Text = titles[idx];
        for (int i = 0; i < 4; i++)
        {
            pages[i].Visible = i == idx;
            tabIcons[i].Selected = i == idx;
            tabIcons[i].QueueRedraw();
            tabLabels[i].AddThemeColorOverride("font_color", i == idx ? TabOn : TabOff);
        }
    }

    // ==================== 四个子页 ====================

    /// <summary>微信 tab：会话列表</summary>
    private Control BuildSessionPage(ContactsData contacts)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pad.AddThemeConstantOverride("margin_top", 8);
        pad.AddThemeConstantOverride("margin_bottom", 16);
        scroll.AddChild(pad);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        pad.AddChild(box);

        foreach (var c in contacts.Contacts)
        {
            var id = c.Id;
            var row = new Button { MouseFilter = MouseFilterEnum.Stop };
            row.CustomMinimumSize = new Vector2(0, 128);
            ApplyEmptyStyle(row);
            row.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.05f) });

            var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            hbox.AddThemeConstantOverride("separation", 20);
            var hboxWrap = UiKit.WrapMargin(hbox, 24, 0, 24, 0);
            row.AddChild(hboxWrap);
            hboxWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // Button 不是容器，子节点要手动铺满

            hbox.AddChild(MakeAvatar(c, 96));

            var mid = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            mid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mid.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            mid.AddThemeConstantOverride("separation", 10);
            hbox.AddChild(mid);

            mid.AddChild(MakeLabel(c.Name, 28, TextDark));
            var preview = MakeLabel(PreviewOf(c), 24, TextGray);
            preview.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            preview.MaxLinesVisible = 1;
            mid.AddChild(preview);

            var time = MakeLabel(c.SessionTime, 22, TextGray);
            time.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            hbox.AddChild(time);

            row.Pressed += () => ContactSelected?.Invoke(id);
            box.AddChild(row);
        }
        return scroll;
    }

    /// <summary>通讯录 tab：联系人列表</summary>
    private Control BuildContactsPage(ContactsData contacts)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pad.AddThemeConstantOverride("margin_top", 8);
        pad.AddThemeConstantOverride("margin_bottom", 16);
        scroll.AddChild(pad);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        pad.AddChild(box);

        var count = MakeLabel($"{contacts.Contacts.Count} 位联系人", 24, TextGray);
        box.AddChild(UiKit.WrapMargin(count, 24, 12, 0, 12));

        foreach (var c in contacts.Contacts)
        {
            var id = c.Id;
            var row = new Button { MouseFilter = MouseFilterEnum.Stop };
            row.CustomMinimumSize = new Vector2(0, 108);
            ApplyEmptyStyle(row);
            row.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.05f) });

            var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            hbox.AddThemeConstantOverride("separation", 20);
            var hboxWrap = UiKit.WrapMargin(hbox, 24, 0, 24, 0);
            row.AddChild(hboxWrap);
            hboxWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // Button 不是容器，子节点要手动铺满

            hbox.AddChild(MakeAvatar(c, 80));
            var name = MakeLabel(c.Name, 28, TextDark);
            name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            hbox.AddChild(name);

            row.Pressed += () => ContactSelected?.Invoke(id);
            box.AddChild(row);
        }
        return scroll;
    }

    /// <summary>发现 tab：朋友圈入口 + 占位条目</summary>
    private Control BuildDiscoverPage()
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 16);
        box.AddThemeConstantOverride("margin_top", 16);

        box.AddChild(MakeDiscoverRow(new Color(0.35f, 0.45f, 0.75f), "朋友圈", true,
            () => MomentsOpened?.Invoke()));
        box.AddChild(MakeDiscoverRow(new Color(0.85f, 0.45f, 0.25f), "视频号", false,
            () => ToastRequested?.Invoke("视频号目前只是摆设～")));
        box.AddChild(MakeDiscoverRow(new Color(0.30f, 0.65f, 0.45f), "游戏", false,
            () => ToastRequested?.Invoke("游戏中心目前只是摆设～")));
        return box;
    }

    /// <summary>我 tab：资料卡 + 占位条目</summary>
    private Control BuildMePage()
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 16);
        box.AddThemeConstantOverride("margin_top", 16);

        // 资料卡
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Colors.White });
        box.AddChild(card);

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 24);
        hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.AddChild(UiKit.WrapMargin(hbox, 28, 28, 28, 28));

        var myAvatar = new TextureRect
        {
            Texture = GD.Load<Texture2D>(WeChatData.MyAvatarPath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(128, 128),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        myAvatar.Material = ChatOverlay.MakeMaskMaterial(128, 128, 16);
        hbox.AddChild(myAvatar);

        var mid = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mid.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        mid.AddThemeConstantOverride("separation", 12);
        hbox.AddChild(mid);
        mid.AddChild(MakeLabel("我", 38, TextDark));
        mid.AddChild(MakeLabel("微信号：fangguoge_2024", 24, TextGray));

        var arrow = MakeLabel("〉", 32, TextGray);
        arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(arrow);

        // 占位条目
        box.AddChild(MakeDiscoverRow(new Color(0.30f, 0.55f, 0.80f), "服务", false,
            () => ToastRequested?.Invoke("服务页目前只是摆设～")));
        box.AddChild(MakeDiscoverRow(new Color(0.85f, 0.65f, 0.25f), "表情收藏", false,
            () => ToastRequested?.Invoke("表情收藏目前只是摆设～")));
        return box;
    }

    /// <summary>发现/我 页的白色条目行</summary>
    private Control MakeDiscoverRow(Color iconColor, string name, bool redDot, Action onClick)
    {
        var row = new Button { MouseFilter = MouseFilterEnum.Stop };
        row.CustomMinimumSize = new Vector2(0, 108);
        row.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Colors.White });
        row.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = Colors.White });
        row.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color(0.93f, 0.93f, 0.94f) });
        row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 22);
        var hboxWrap = UiKit.WrapMargin(hbox, 28, 0, 28, 0);
        row.AddChild(hboxWrap);
        hboxWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // Button 不是容器，子节点要手动铺满

        var icon = new RowIcon(iconColor) { CustomMinimumSize = new Vector2(56, 56) };
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(icon);

        var label = MakeLabel(name, 30, TextDark);
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(label);

        if (redDot)
        {
            var dot = new RedDot { CustomMinimumSize = new Vector2(16, 16) };
            dot.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            hbox.AddChild(dot);
        }

        var expander = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hbox.AddChild(expander);

        var arrow = MakeLabel("〉", 32, TextGray);
        arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(arrow);

        row.Pressed += () => onClick?.Invoke();
        return row;
    }

    // ==================== 小工具 ====================

    /// <summary>会话列表预览文字（同桌走聊天文件，其他人走内嵌消息）</summary>
    private static string PreviewOf(ContactData c)
    {
        if (!string.IsNullOrEmpty(c.ChatFile))
        {
            var chat = ChatOverlay.LoadChatData(c.ChatFile);
            for (int i = chat.Messages.Count - 1; i >= 0; i--)
            {
                if (chat.Messages[i].Type == "text")
                    return chat.Messages[i].Sender == "me" ? "我：" + chat.Messages[i].Text : chat.Messages[i].Text;
            }
            return "[图片]";
        }
        return c.Preview;
    }

    /// <summary>联系人头像：有图用图，没图用首字色块</summary>
    public static Control MakeAvatar(ContactData c, float size)
    {
        if (!string.IsNullOrEmpty(c.Avatar) && ResourceLoader.Exists(c.Avatar))
        {
            var tex = new TextureRect
            {
                Texture = GD.Load<Texture2D>(c.Avatar),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = SizeFlags.ShrinkBegin, // 顶对齐，别被行高拉成竖条
                MouseFilter = MouseFilterEnum.Ignore,
            };
            tex.Material = ChatOverlay.MakeMaskMaterial(size, size, 12);
            return tex;
        }
        var initial = new InitialAvatar(c.Name, size);
        initial.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return initial;
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button MakeFlatButton(string text, int fontSize)
    {
        var btn = new Button
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.AddThemeColorOverride("font_color", TextDark);
        ApplyEmptyStyle(btn);
        return btn;
    }

    private static void ApplyEmptyStyle(Button btn)
    {
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
    }
}

/// <summary>底部标签的小图标（代码画的极简几何形）</summary>
public partial class TabIcon : Control
{
    public enum Kind { Chat, Contacts, Discover, Me }

    private static readonly Color OnColor = new Color(0.16f, 0.62f, 0.30f);
    private static readonly Color OffColor = new Color(0.45f, 0.45f, 0.48f);

    private readonly Kind kind;
    private bool selected;

    public bool Selected
    {
        get => selected;
        set => selected = value;
    }

    public TabIcon(Kind k) { kind = k; }

    public override void _Draw()
    {
        var color = selected ? OnColor : OffColor;
        float w = Size.X, h = Size.Y;
        var c = new Vector2(w / 2, h / 2);

        switch (kind)
        {
            case Kind.Chat:
                DrawCircle(c + new Vector2(-4, 2), 12, color);
                DrawCircle(c + new Vector2(8, -6), 8, color);
                break;
            case Kind.Contacts:
                DrawCircle(c + new Vector2(0, -8), 7, color);
                DrawRect(new Rect2(c + new Vector2(-11, 2), new Vector2(22, 12)), color);
                break;
            case Kind.Discover:
                DrawArc(c, 13, 0, Mathf.Tau, 48, color, 3.5f);
                DrawColoredPolygon(new Vector2[]
                {
                    c + new Vector2(-6, 6), c + new Vector2(8, -8), c + new Vector2(2, 2),
                }, color);
                break;
            case Kind.Me:
                DrawCircle(c + new Vector2(0, -9), 7, color);
                DrawHalfDisc(c + new Vector2(0, 14), 12, color);
                break;
        }
    }

    /// <summary>上半圆盘（肩膀）</summary>
    private void DrawHalfDisc(Vector2 center, float r, Color color)
    {
        var points = new List<Vector2> { center };
        for (int i = 0; i <= 12; i++)
        {
            float a = Mathf.Pi * i / 12f;
            points.Add(center + new Vector2(-Mathf.Cos(a) * r, -Mathf.Sin(a) * r));
        }
        DrawColoredPolygon(points.ToArray(), color);
    }
}

/// <summary>发现页条目左侧的彩色小方块图标</summary>
public partial class RowIcon : Control
{
    private readonly Color color;
    public RowIcon(Color c) { color = c; }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), color);
        DrawCircle(Size / 2, 8, new Color(1, 1, 1, 0.85f));
    }
}

/// <summary>朋友圈红点</summary>
public partial class RedDot : Control
{
    public override void _Draw()
    {
        DrawCircle(Size / 2, Mathf.Min(Size.X, Size.Y) / 2, new Color(0.92f, 0.25f, 0.20f));
    }
}
