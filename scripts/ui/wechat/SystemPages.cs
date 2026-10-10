using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 订阅号消息页（第九轮）：公众号 / 服务号折叠入口点开的文章列表。
///
/// 数据来自 data/system_wechat.json 的 articles——标题就是嘲讽点：
/// 导师半夜转发的那两篇，这里都能找到"出处"。
/// 文章正文是摆设，点击只弹 toast。
/// </summary>
public partial class SubscriptionsPage : Control
{
    /// <summary>点了返回</summary>
    public event Action BackPressed;
    /// <summary>点了某篇文章（参数 = toast 文案）</summary>
    public event Action<string> ToastRequested;

    private static readonly Color PageBg = new Color(0.935f, 0.935f, 0.94f);
    private static readonly Color TextDark = new Color(0.11f, 0.11f, 0.13f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);

    public void Build(SystemWechatData data)
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(SystemPagesKit.MakePageBg(PageBg));

        var header = SystemPagesKit.BuildHeader(DataStore.Text("sys.subscriptions_title"), () => BackPressed?.Invoke());
        AddChild(header);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 102;
        AddChild(scroll);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(box);

        foreach (var a in data.Articles)
            box.AddChild(MakeArticleRow(a));
    }

    private Control MakeArticleRow(SubscriptionArticle a)
    {
        var row = new Button { MouseFilter = MouseFilterEnum.Stop };
        row.CustomMinimumSize = new Vector2(0, 150);
        SystemPagesKit.ApplyWhiteRowStyle(row);

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 10);
        var wrap = UiKit.WrapMargin(vbox, 28, 22, 28, 22);
        row.AddChild(wrap);
        wrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var title = new Label
        {
            Text = a.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", TextDark);
        vbox.AddChild(title);

        var meta = SystemPagesKit.MakeLabel(DataStore.Text("sys.article_meta", a.Source, a.Time, a.Reads), 22, TextGray);
        vbox.AddChild(meta);

        row.Pressed += () => ToastRequested?.Invoke(DataStore.Text("sys.article_toast"));
        return row;
    }
}

/// <summary>
/// 微信运动排行页（第九轮）：今日步数榜。
/// 嘲讽点——导师以两万八千步登顶，男主 385 步垫底。
/// </summary>
public partial class StepsRankingPage : Control
{
    public event Action BackPressed;
    public event Action<string> ToastRequested;

    private static readonly Color PageBg = new Color(0.935f, 0.935f, 0.94f);
    private static readonly Color TextDark = new Color(0.11f, 0.11f, 0.13f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);

    public void Build(SystemWechatData data)
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(SystemPagesKit.MakePageBg(PageBg));

        var header = SystemPagesKit.BuildHeader(DataStore.Text("sys.steps_title"), () => BackPressed?.Invoke());
        AddChild(header);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 102;
        AddChild(scroll);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(box);

        // 顶部卡片：自己的步数 + 一句丧话
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var cardStyle = new StyleBoxFlat { BgColor = Colors.White };
        cardStyle.ContentMarginLeft = 28;
        cardStyle.ContentMarginRight = 28;
        cardStyle.ContentMarginTop = 24;
        cardStyle.ContentMarginBottom = 24;
        card.AddThemeStyleboxOverride("panel", cardStyle);
        var cardBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        cardBox.AddThemeConstantOverride("separation", 8);
        card.AddChild(cardBox);
        cardBox.AddChild(SystemPagesKit.MakeLabel(DataStore.Text("sys.steps_today", data.MySteps), 34, TextDark));
        if (!string.IsNullOrEmpty(data.MyStepsCaption))
            cardBox.AddChild(SystemPagesKit.MakeLabel(data.MyStepsCaption, 22, TextGray));
        box.AddChild(card);

        var listTitle = SystemPagesKit.MakeLabel(DataStore.Text("sys.steps_rank_title"), 24, TextGray);
        box.AddChild(UiKit.WrapMargin(listTitle, 28, 20, 0, 8));

        int rank = 0;
        foreach (var s in data.Steps)
        {
            rank++;
            box.AddChild(MakeStepRow(s, rank));
        }
    }

    private Control MakeStepRow(StepEntry s, int rank)
    {
        var row = new Button { MouseFilter = MouseFilterEnum.Stop };
        row.CustomMinimumSize = new Vector2(0, 116);
        SystemPagesKit.ApplyWhiteRowStyle(row);

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 18);
        var wrap = UiKit.WrapMargin(hbox, 28, 0, 28, 0);
        row.AddChild(wrap);
        wrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 前三名用金/银/铜色数字（游戏字体没有 emoji 字形，别用奖牌符号）
        var rankColor = rank switch
        {
            1 => new Color(0.85f, 0.62f, 0.10f),
            2 => new Color(0.55f, 0.57f, 0.62f),
            3 => new Color(0.72f, 0.45f, 0.25f),
            _ => TextDark,
        };
        var rankLabel = SystemPagesKit.MakeLabel($"{rank}", 28, rankColor);
        rankLabel.CustomMinimumSize = new Vector2(52, 0);
        rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
        rankLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(rankLabel);

        hbox.AddChild(MakeStepAvatar(s.Name, 72));

        var mid = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mid.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        mid.AddThemeConstantOverride("separation", 6);
        hbox.AddChild(mid);
        mid.AddChild(SystemPagesKit.MakeLabel(s.Name, 28, TextDark));
        if (!string.IsNullOrEmpty(s.Remark))
            mid.AddChild(SystemPagesKit.MakeLabel(s.Remark, 22, TextGray));

        var count = SystemPagesKit.MakeLabel(DataStore.Text("sys.steps_count", s.Count), 26, rank == 1 ? new Color(0.92f, 0.45f, 0.15f) : TextGray);
        count.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(count);

        row.Pressed += () => ToastRequested?.Invoke(DataStore.Text("sys.steps_like_toast"));
        return row;
    }

    /// <summary>排行里的头像：能对上联系人的用照片，"我"用海绵宝宝，其余首字色块</summary>
    private static Control MakeStepAvatar(string name, float size)
    {
        if (name == "我")
        {
            var me = new TextureRect
            {
                Texture = GD.Load<Texture2D>(WeChatData.MyAvatarPath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            me.Material = ChatOverlay.MakeMaskMaterial(size, size, 10);
            return me;
        }
        var c = WeChatData.FindByName(name);
        if (c != null && !string.IsNullOrEmpty(c.Avatar) && ResourceLoader.Exists(c.Avatar))
        {
            var tex = new TextureRect
            {
                Texture = GD.Load<Texture2D>(c.Avatar),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            tex.Material = ChatOverlay.MakeMaskMaterial(size, size, 10);
            return tex;
        }
        var initial = new InitialAvatar(name, size);
        initial.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return initial;
    }
}

/// <summary>系统页共用小件：顶栏（返回 + 标题）、白底行样式、Label 工厂</summary>
public static class SystemPagesKit
{
    private static readonly Color HeaderBg = new Color(0.925f, 0.925f, 0.935f);
    private static readonly Color HeaderText = new Color(0.12f, 0.12f, 0.14f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);

    public static Control BuildHeader(string title, Action onBack)
    {
        var header = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = HeaderBg };
        header.AddThemeStyleboxOverride("panel", style);
        header.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        header.OffsetBottom = 102;

        var box = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        header.AddChild(box);

        var back = new Button
        {
            Text = "‹",
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        back.AddThemeFontSizeOverride("font_size", 44);
        back.AddThemeColorOverride("font_color", HeaderText);
        var empty = new StyleBoxEmpty();
        back.AddThemeStyleboxOverride("normal", empty);
        back.AddThemeStyleboxOverride("hover", empty);
        back.AddThemeStyleboxOverride("focus", empty);
        back.OffsetLeft = 20;
        back.OffsetTop = 8;
        back.OffsetRight = 84;
        back.OffsetBottom = 92;
        back.Pressed += () => onBack?.Invoke();
        box.AddChild(back);

        var label = MakeLabel(title, 30, HeaderText);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.AddChild(label);

        var line = new ColorRect { Color = new Color(0, 0, 0, 0.07f), MouseFilter = Control.MouseFilterEnum.Ignore };
        line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        line.OffsetTop = 100;
        line.OffsetBottom = 102;
        box.AddChild(line);

        return header;
    }

    /// <summary>整页底色（铺满父容器）</summary>
    public static Control MakePageBg(Color color)
    {
        var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    public static void ApplyWhiteRowStyle(Button row)
    {
        var normal = new StyleBoxFlat { BgColor = Colors.White };
        normal.BorderWidthBottom = 1;
        normal.BorderColor = new Color(0, 0, 0, 0.05f);
        row.AddThemeStyleboxOverride("normal", normal);
        row.AddThemeStyleboxOverride("hover", normal);
        row.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color(0.93f, 0.93f, 0.94f) });
        row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public static Label MakeLabel(string text, int fontSize, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
