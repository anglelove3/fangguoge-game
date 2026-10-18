using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 手机桌面端 · 锁屏 + 桌面（第十五轮）
///
/// 【这是什么】
/// 手机开机先落在锁屏：壁纸、时间、日期，微信有新消息时浮一张通知卡。
/// 点一下（或者往上划）解锁 → 桌面：12 个 App 图标，微信图标上挂着未读角标。
/// 点图标去哪个 App，由 ChatOverlay 路由（桌面只负责"报名字"）。
///
/// 【小细节】
///   - 时间是真实系统时间（你手机几点，它就是几点），日期不带年份；
///   - 微信角标和锁屏通知卡都盯着 LiveEvents 的未读表，读完消息自己就消了。
/// </summary>
/// <summary>
/// 手机壁纸的昼夜双版（第十八轮清遗留①）：
/// 故事时钟 17:00 前用白天壁纸，之后用黄昏壁纸——
/// 以前只有黄昏一张，时钟报 11:35 配着漫天晚霞，穿帮。
/// </summary>
public static class PhoneWallpaper
{
    public const string Day = "res://assets/art/backgrounds/wallpaper_day_v1.png";
    public const string Night = "res://assets/art/backgrounds/campus_dusk_v1.png";

    public static string Current => StoryClock.IsDaytime() ? Day : Night;
}

public partial class LockScreen : Control
{
    /// <summary>向上滑动 / 点一下 = 解锁</summary>
    public event Action UnlockRequested;
    /// <summary>点通知卡 = 直接去微信</summary>
    public event Action WeChatRequested;

    private const float WallpaperDimDay = 0.10f;   // 白天壁纸透亮一点
    private const float WallpaperDimNight = 0.28f; // 黄昏壁纸压暗一点，字才读得清

    private Label clockLabel;
    private Label dateLabel;
    private Label batteryPct;   // 锁屏右上角的电量百分比（第二十轮 F4）
    private PanelContainer noticeCard;
    private Label noticeText;
    private Label hintLabel;
    private TextureRect wallRect;   // 壁纸本体（昼夜切换时换图）
    private ColorRect wallDim;      // 壁纸上的压暗层（昼夜切换时调浓度）
    private bool? lastDay;          // 上一次的昼夜状态（null = 还没对过表）
    private bool dragging;
    private float pressY;
    private float pressX;
    private string lastClock = "";
    private string lastDate = "";
    private string lastBatteryPct = "";
    private int lastUnreadOnLock = -1;   // 锁屏通知卡跟随未读变化（第二十四轮 F22）

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
        Refresh();
    }

    private void Build()
    {
        // ---- 壁纸（昼夜双版，_Process 里跟着故事时钟换）----
        string wallpaper = PhoneWallpaper.Current;
        lastDay = StoryClock.IsDaytime();
        if (ResourceLoader.Exists(wallpaper))
        {
            wallRect = new TextureRect
            {
                Texture = GD.Load<Texture2D>(wallpaper),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            wallRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(wallRect);
        }
        wallDim = new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.09f, lastDay == true ? WallpaperDimDay : WallpaperDimNight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        wallDim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(wallDim);

        // ---- 右上角小状态行：电量百分比 + 电池图标（第二十轮 F4）----
        var statusRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        statusRow.AddThemeConstantOverride("separation", 8);
        statusRow.AnchorLeft = 1f;
        statusRow.AnchorRight = 1f;
        statusRow.OffsetLeft = -190;
        statusRow.OffsetTop = 18;
        statusRow.OffsetRight = -30;
        statusRow.OffsetBottom = 52;
        AddChild(statusRow);

        batteryPct = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        batteryPct.AddThemeFontSizeOverride("font_size", 24);
        batteryPct.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f));
        batteryPct.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.4f));
        batteryPct.AddThemeConstantOverride("outline_size", 6);
        batteryPct.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        statusRow.AddChild(batteryPct);

        statusRow.AddChild(new BatteryGlyph { CustomMinimumSize = new Vector2(52, 30) });

        // ---- 时间 / 日期 ----
        var top = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AnchorLeft = 0.5f;
        top.AnchorRight = 0.5f;
        top.OffsetLeft = -320;
        top.OffsetRight = 320;
        top.OffsetTop = 108;
        top.GrowHorizontal = GrowDirection.Both;
        top.AddThemeConstantOverride("separation", 6);
        AddChild(top);

        clockLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        clockLabel.AddThemeFontSizeOverride("font_size", 108);
        clockLabel.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f));
        clockLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.45f));
        clockLabel.AddThemeConstantOverride("outline_size", 10);
        top.AddChild(clockLabel);

        dateLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        dateLabel.AddThemeFontSizeOverride("font_size", 27);
        dateLabel.AddThemeColorOverride("font_color", new Color(1f, 0.98f, 0.94f));
        dateLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.4f));
        dateLabel.AddThemeConstantOverride("outline_size", 8);
        top.AddChild(dateLabel);

        // ---- 通知卡（有未读微信时才浮出来）----
        noticeCard = new PanelContainer { Visible = false };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.13f, 0.17f, 0.82f),
            CornerRadiusTopLeft = 22,
            CornerRadiusTopRight = 22,
            CornerRadiusBottomLeft = 22,
            CornerRadiusBottomRight = 22,
            ContentMarginLeft = 26,
            ContentMarginRight = 26,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
        };
        noticeCard.AddThemeStyleboxOverride("panel", cardStyle);
        noticeCard.AnchorLeft = 0.5f;
        noticeCard.AnchorRight = 0.5f;
        noticeCard.AnchorTop = 1f;
        noticeCard.AnchorBottom = 1f;
        noticeCard.OffsetLeft = -320;
        noticeCard.OffsetRight = 320;
        noticeCard.OffsetTop = -250;
        noticeCard.OffsetBottom = -150;
        noticeCard.GrowHorizontal = GrowDirection.Both;
        noticeCard.GrowVertical = GrowDirection.Begin;
        noticeCard.MouseDefaultCursorShape = CursorShape.PointingHand;
        noticeCard.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                AcceptEvent();
                WeChatRequested?.Invoke();
            }
        };
        AddChild(noticeCard);

        var cardRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        cardRow.AddThemeConstantOverride("separation", 16);
        noticeCard.AddChild(cardRow);

        var icon = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var iconStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.76f, 0.33f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
        };
        icon.AddThemeStyleboxOverride("panel", iconStyle);
        icon.CustomMinimumSize = new Vector2(56, 56);
        cardRow.AddChild(icon);
        var iconChar = new Label
        {
            Text = DataStore.Text("wx.icon_glyph"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        iconChar.AddThemeFontSizeOverride("font_size", 30);
        iconChar.AddThemeColorOverride("font_color", Colors.White);
        icon.AddChild(iconChar);

        var textBox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        textBox.AddThemeConstantOverride("separation", 2);
        cardRow.AddChild(textBox);

        var appName = new Label { Text = DataStore.Text("wx.tab_chats"), MouseFilter = MouseFilterEnum.Ignore };
        appName.AddThemeFontSizeOverride("font_size", 21);
        appName.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.9f));
        textBox.AddChild(appName);

        noticeText = new Label { MouseFilter = MouseFilterEnum.Ignore };
        noticeText.AddThemeFontSizeOverride("font_size", 25);
        noticeText.AddThemeColorOverride("font_color", Colors.White);
        textBox.AddChild(noticeText);

        // ---- 底部解锁提示 ----
        hintLabel = new Label
        {
            Text = DataStore.Text("phone.lock_hint"),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hintLabel.AddThemeFontSizeOverride("font_size", 24);
        hintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f, 0.9f));
        hintLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.4f));
        hintLabel.AddThemeConstantOverride("outline_size", 8);
        hintLabel.AnchorLeft = 0.5f;
        hintLabel.AnchorRight = 0.5f;
        hintLabel.AnchorTop = 1f;
        hintLabel.AnchorBottom = 1f;
        hintLabel.OffsetLeft = -200;
        hintLabel.OffsetRight = 200;
        hintLabel.OffsetTop = -96;
        hintLabel.OffsetBottom = -52;
        hintLabel.GrowHorizontal = GrowDirection.Both;
        hintLabel.GrowVertical = GrowDirection.Begin;
        AddChild(hintLabel);

        // 呼吸：提示轻轻一亮一暗
        var pulse = CreateTween();
        pulse.SetLoops(-1);
        pulse.TweenProperty(hintLabel, "modulate:a", 0.55f, 1.1).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(hintLabel, "modulate:a", 1.0f, 1.1).SetTrans(Tween.TransitionType.Sine);
    }

    public override void _Process(double delta)
    {
        // 故事内时间（data/story_clock.json），没有条目才回退系统时间
        string clock = StoryClock.ClockText();
        if (clock != lastClock)
        {
            lastClock = clock;
            clockLabel.Text = clock;
        }
        string date = StoryClock.DateText();
        if (date != lastDate)
        {
            lastDate = date;
            dateLabel.Text = date;
        }

        // 电量百分比（第二十轮 F4）：低电量连数字一起变红
        int lv = GameManager.Instance?.PhoneBattery ?? 100;
        string pct = lv + "%";
        if (pct != lastBatteryPct)
        {
            lastBatteryPct = pct;
            // 第三轮反馈：低电量要让人「感觉到」——数字变红 + 后面跟一句提醒
            batteryPct.Text = lv <= GameManager.BatteryLowThreshold ? pct + "  电量不足" : pct;
            batteryPct.AddThemeColorOverride("font_color",
                lv <= GameManager.BatteryLowThreshold
                    ? new Color(1f, 0.45f, 0.4f)
                    : new Color(1f, 0.99f, 0.96f));
        }

        // 未读数实时驱动通知卡（第二十四轮 F22）：
        // 以前只在掏手机那一刻刷新，读完消息锁屏还挂着「1 条新消息」骗人
        int unread = LiveEvents.TotalUnread;
        if (unread != lastUnreadOnLock)
        {
            lastUnreadOnLock = unread;
            Refresh();
        }

        // 壁纸昼夜切换（清遗留①）：故事走到傍晚，锁屏跟着换黄昏
        bool day = StoryClock.IsDaytime();
        if (day != lastDay)
        {
            lastDay = day;
            ApplyWallpaper();
        }
    }

    private void ApplyWallpaper()
    {
        string path = PhoneWallpaper.Current;
        if (wallRect != null && ResourceLoader.Exists(path))
            wallRect.Texture = GD.Load<Texture2D>(path);
        if (wallDim != null)
            wallDim.Color = new Color(0.03f, 0.04f, 0.09f, lastDay == true ? WallpaperDimDay : WallpaperDimNight);
    }

    private static string WeekCn(int weekday) => weekday switch
    {
        0 => "星期日",
        1 => "星期一",
        2 => "星期二",
        3 => "星期三",
        4 => "星期四",
        5 => "星期五",
        _ => "星期六",
    };

    /// <summary>未读有变化时刷新通知卡（开手机时 / 补投递之后调用）</summary>
    public void Refresh()
    {
        int n = LiveEvents.TotalUnread;
        noticeCard.Visible = n > 0;
        if (n > 0)
            noticeText.Text = DataStore.Text("phone.lock_unread", n);
    }

    // ==================== 解锁手势 ====================

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                dragging = true;
                pressY = mb.Position.Y;
                pressX = mb.Position.X;
            }
            else if (dragging)
            {
                dragging = false;
                float dy = pressY - mb.Position.Y;
                float dx = Mathf.Abs(pressX - mb.Position.X);
                // 往上划一段 = 解锁；原地轻点一下也解锁（别让玩家摸不着头脑）
                if (dy > 40f || (dy > -8f && dy < 12f && dx < 12f))
                {
                    AcceptEvent();
                    UnlockRequested?.Invoke();
                }
            }
        }
    }
}

/// <summary>
/// 桌面：壁纸压暗 + 顶部状态栏 + 一板 App 图标。
/// 图标点谁就把 id 抛给 ChatOverlay，自己不关心开着什么 App。
/// </summary>
public partial class PhoneDesktop : Control
{
    /// <summary>点了某个 App 图标（参数 = apps.json 里的 id）</summary>
    public event Action<string> AppLaunched;

    private const float WallpaperDimDay = 0.20f;   // 白天壁纸压暗少一点
    private const float WallpaperDimNight = 0.42f; // 黄昏壁纸压暗多一点，图标才看得清

    private readonly Dictionary<string, AppIcon> icons = new();
    private Label statusClock;
    private TextureRect wallRect;
    private ColorRect wallDim;
    private bool? lastDay;
    private string lastClock = "";
    private int lastUnreadOnDesktop = -1;   // 桌面角标跟随未读变化（第二十四轮 F22）

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Build();
        Refresh();
    }

    private void Build()
    {
        // ---- 壁纸（压暗一点，图标才看得清；昼夜双版跟锁屏同步换）----
        string wallpaper = PhoneWallpaper.Current;
        lastDay = StoryClock.IsDaytime();
        if (ResourceLoader.Exists(wallpaper))
        {
            wallRect = new TextureRect
            {
                Texture = GD.Load<Texture2D>(wallpaper),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            wallRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(wallRect);
            wallDim = new ColorRect
            {
                Color = new Color(0.04f, 0.05f, 0.10f, lastDay == true ? WallpaperDimDay : WallpaperDimNight),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            wallDim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(wallDim);
        }

        // ---- 顶部状态栏（时间 + 信号电量）----
        var bar = new Control { MouseFilter = MouseFilterEnum.Ignore };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 56;
        AddChild(bar);

        statusClock = new Label { MouseFilter = MouseFilterEnum.Ignore };
        statusClock.AddThemeFontSizeOverride("font_size", 26);
        statusClock.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f));
        statusClock.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.35f));
        statusClock.AddThemeConstantOverride("outline_size", 6);
        statusClock.OffsetLeft = 34;
        statusClock.OffsetTop = 14;
        statusClock.OffsetRight = 200;
        statusClock.OffsetBottom = 48;
        bar.AddChild(statusClock);

        var battery = new BatteryGlyph
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -108,
            OffsetTop = 14,
            OffsetRight = -34,
            OffsetBottom = 46,
        };
        bar.AddChild(battery);

        // ---- App 图标板（居中摆放，12 个图标 4×3）----
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.AnchorLeft = 0f;
        center.AnchorRight = 1f;
        center.AnchorTop = 0f;
        center.AnchorBottom = 0f;
        center.OffsetTop = 100;
        center.OffsetBottom = 806;
        center.GrowHorizontal = GrowDirection.Both;
        AddChild(center);

        var grid = new GridContainer { Columns = 4, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 26);
        grid.AddThemeConstantOverride("v_separation", 22);
        center.AddChild(grid);

        foreach (var app in PhoneData.Data.Apps)
        {
            if (app == null || string.IsNullOrEmpty(app.Id))
                continue;
            var icon = new AppIcon(app);
            icon.Chosen += id => AppLaunched?.Invoke(id);
            grid.AddChild(icon);
            icons[app.Id] = icon;
        }
    }

    public override void _Process(double delta)
    {
        string clock = StoryClock.ClockText();
        if (clock != lastClock)
        {
            lastClock = clock;
            statusClock.Text = clock;
        }

        // 微信角标跟随未读变化（第二十四轮 F22，与锁屏同源）
        int unread = LiveEvents.TotalUnread;
        if (unread != lastUnreadOnDesktop)
        {
            lastUnreadOnDesktop = unread;
            Refresh();
        }

        // 壁纸昼夜切换（清遗留①）：和锁屏同一套规则
        bool day = StoryClock.IsDaytime();
        if (day != lastDay)
        {
            lastDay = day;
            string path = PhoneWallpaper.Current;
            if (wallRect != null && ResourceLoader.Exists(path))
                wallRect.Texture = GD.Load<Texture2D>(path);
            if (wallDim != null)
                wallDim.Color = new Color(0.04f, 0.05f, 0.10f, day ? WallpaperDimDay : WallpaperDimNight);
        }
    }

    /// <summary>角标刷新：微信挂未读总数（打开桌面前调用）</summary>
    public void Refresh()
    {
        if (icons.TryGetValue("wechat", out var wechat))
            wechat.SetBadge(LiveEvents.TotalUnread);
    }
}

/// <summary>一个桌面 App 图标：圆角底板 + 一个字 + 名字；支持右上角未读角标</summary>
public partial class AppIcon : Control
{
    public event Action<string> Chosen;

    private readonly PhoneData.AppEntry def;
    private Label badgeLabel;
    private PanelContainer badge;

    public AppIcon(PhoneData.AppEntry def)
    {
        this.def = def;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(140, 182);

        var baseColor = PhoneData.ParseColor(def.Color);
        var glyphColor = def.DarkGlyph ? new Color(0.15f, 0.12f, 0.06f) : Colors.White;

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var btn = new Button
        {
            CustomMinimumSize = new Vector2(132, 132),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var normal = new StyleBoxFlat { BgColor = baseColor };
        normal.SetCornerRadiusAll(30);
        var hover = new StyleBoxFlat { BgColor = baseColor.Lightened(0.10f) };
        hover.SetCornerRadiusAll(30);
        var pressed = new StyleBoxFlat { BgColor = baseColor.Darkened(0.14f) };
        pressed.SetCornerRadiusAll(30);
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.Pressed += () => Chosen?.Invoke(def.Id);
        column.AddChild(btn);

        // 第三轮反馈 1：优先用拟真图标（AI 生成切图，assets/art/ui/app_{id}.png），
        // 没有图的（音乐/支付等）保留原来的圆角色块 + 一个字
        string iconPath = $"res://assets/art/ui/app_{def.Id}.png";
        if (ResourceLoader.Exists(iconPath))
        {
            var pic = new TextureRect
            {
                Texture = GD.Load<Texture2D>(iconPath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn.AddChild(pic);
            // 图标自带圆角底，按钮底色透出来会露边——压成透明
            foreach (var st in new[] { "normal", "hover", "pressed", "focus" })
            {
                var empty = new StyleBoxEmpty();
                btn.AddThemeStyleboxOverride(st, empty);
            }
        }
        else
        {
            var glyph = new Label
            {
                Text = def.Glyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            glyph.AddThemeFontSizeOverride("font_size", 58);
            glyph.AddThemeColorOverride("font_color", glyphColor);
            glyph.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn.AddChild(glyph);
        }

        // 未读角标（微信专用，SetBadge 控制）
        badge = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        var badgeStyle = new StyleBoxFlat { BgColor = new Color(0.95f, 0.23f, 0.19f) };
        badgeStyle.SetCornerRadiusAll(14);
        badgeStyle.ContentMarginLeft = 8;
        badgeStyle.ContentMarginRight = 8;
        badgeStyle.ContentMarginTop = 2;
        badgeStyle.ContentMarginBottom = 2;
        badge.AddThemeStyleboxOverride("panel", badgeStyle);
        badge.AnchorLeft = 1f;
        badge.AnchorRight = 1f;
        badge.AnchorTop = 0f;
        badge.AnchorBottom = 0f;
        badge.OffsetLeft = -30;
        badge.OffsetTop = -6;
        badge.OffsetRight = 6;
        badge.OffsetBottom = 28;
        badge.GrowHorizontal = GrowDirection.Begin;
        badge.GrowVertical = GrowDirection.Both;
        btn.AddChild(badge);

        badgeLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
        badgeLabel.AddThemeFontSizeOverride("font_size", 20);
        badgeLabel.AddThemeColorOverride("font_color", Colors.White);
        badge.AddChild(badgeLabel);

        var name = new Label
        {
            Text = def.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 21);
        name.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f));
        name.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.55f));
        name.AddThemeConstantOverride("outline_size", 7);
        column.AddChild(name);

        UiSounds.Wire(btn);
    }

    /// <summary>右上角未读角标：0 = 不显示</summary>
    public void SetBadge(int count)
    {
        if (badge == null)
            return;
        badge.Visible = count > 0;
        if (count > 0)
            badgeLabel.Text = count > 99 ? "99+" : count.ToString();
    }
}

/// <summary>
/// 状态栏右侧的小电池图标（画出来的，不用图）。
/// 第二十轮 F4：电量是真的了——读 GameManager.PhoneBattery，
/// 内格按电量画长度，≤25% 变红（低电量焦虑是剧情的一部分）。
/// </summary>
public partial class BatteryGlyph : Control
{
    private int lastLevel = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        int lv = GameManager.Instance?.PhoneBattery ?? 100;
        if (lv != lastLevel)
        {
            lastLevel = lv;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        int level = GameManager.Instance?.PhoneBattery ?? 100;
        var w = Size.X;
        var h = Size.Y;
        var body = new Rect2(0, h * 0.18f, w - 6, h * 0.64f);
        DrawStyleBox(MakeOutline(), body);

        // 内格：宽度 = 电量比例；低电量换红色
        float fillW = (body.Size.X - 9f) * Mathf.Clamp(level, 0, 100) / 100f;
        Color fillColor = level <= GameManager.BatteryLowThreshold
            ? new Color(0.95f, 0.30f, 0.25f)
            : new Color(1f, 0.99f, 0.96f);
        if (fillW >= 1f)
        {
            var inner = new StyleBoxFlat { BgColor = fillColor };
            inner.SetCornerRadiusAll(4);
            DrawStyleBox(inner, new Rect2(body.Position.X + 4.5f, body.Position.Y + 4.5f, fillW, body.Size.Y - 9f));
        }

        // 电池头
        var tip = new Rect2(body.End.X + 2, h * 0.36f, 4, h * 0.28f);
        var tipStyle = new StyleBoxFlat { BgColor = new Color(1f, 0.99f, 0.96f, 0.85f) };
        tipStyle.SetCornerRadiusAll(2);
        DrawStyleBox(tipStyle, tip);
    }

    private static StyleBoxFlat MakeOutline()
    {
        var s = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.25f) };
        s.SetCornerRadiusAll(6);
        return s;
    }
}
