using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 序章电脑 · 全屏可交互界面（第十五轮）
///
/// 【这是什么】
/// 序章深夜实验室里那台老电脑，现在能真的用了：
///   · 开关机 —— 屏幕黑着时按电源键开机（有 BIOS 自检），开始菜单里能关机 / 重启
///   · 桌面 —— 我的电脑 / 测试数据 / 论文 / 邮箱 / 回收站 / 开始懂了.mp3
///   · 每个文件点开都有内容 —— 数据、图表、论文批注、邮件，都能看
///   · 桌面上还贴着一张便利贴：「别急，慢慢来。—— 去年的你」
///
/// 【输入抑制】
/// 和背包/手机一套：打开期间 DialogueManager.SetUiSuppressed(true)，
/// 背景对白和热点全部锁死；Esc 逐层退（窗口 → 桌面 → 起身离开）。
///
/// 【数据在哪】
///   data/computer/computer.json —— 开机自检、桌面、文件夹、邮箱，全在里面。
/// </summary>
public partial class ComputerOverlay : Control
{
    /// <summary>收起（fade 结束、即将销毁）时触发</summary>
    public event Action Closed;

    // ---------- 显示器尺寸（1920×1080 视口下的设计尺寸，小了会等比缩） ----------
    private const float MonitorW = 1240f;
    private const float MonitorH = 860f;
    private const float BezelSide = 30f;   // 左右边框
    private const float BezelTop = 26f;    // 上边框
    private const float BezelBottom = 88f; // 下边框（品牌 + 电源键那一条）
    private const float ScreenW = MonitorW - BezelSide * 2f;   // 1180
    private const float ScreenH = MonitorH - BezelTop - BezelBottom; // 746

    private const float TaskbarH = 42f;
    private const float WinW = 840f;
    private const float WinH = 620f;
    private const float WinX = 120f;
    private const float WinY = 46f;
    private const float TitleH = 40f;

    // ---------- 经典配色 ----------
    private static readonly Color BezelColor = new(0.13f, 0.145f, 0.17f);
    private static readonly Color BezelEdge = new(0.30f, 0.33f, 0.38f);
    private static readonly Color TaskbarColor = new(0.12f, 0.30f, 0.72f);
    private static readonly Color TaskbarEdge = new(0.28f, 0.47f, 0.88f);
    private static readonly Color StartGreen = new(0.24f, 0.62f, 0.28f);
    private static readonly Color TitleBlue = new(0.13f, 0.31f, 0.72f);
    private static readonly Color WinBody = new(0.93f, 0.94f, 0.96f);
    private static readonly Color WinLine = new(0.72f, 0.75f, 0.80f);
    private static readonly Color InkDark = new(0.13f, 0.15f, 0.19f);
    private static readonly Color InkMid = new(0.36f, 0.39f, 0.45f);
    private static readonly Color InkRed = new(0.83f, 0.24f, 0.20f);
    private static readonly Color NoteGrey = new(0.55f, 0.58f, 0.63f);

    // ---------- 节点 ----------
    private Control monitorRoot;
    private ScreenView screen;
    private Control desktopLayer;
    private Control windowLayer;
    private Control startMenu;
    private BootView bootView;
    private OffView offView;
    private PowerLed powerLed;

    // 任务栏
    private Label clockLabel;
    private string lastClock = "";

    // ---------- 状态 ----------
    private const string StOff = "off";
    private const string StBooting = "booting";
    private const string StOn = "on";
    private const string StShutdown = "shutdown";

    private string state = StOn;
    private bool closing;
    private bool suppressing;
    private bool opened;
    private bool restartAfterShutdown;

    // 窗口回退栈：每个元素 = 一个能重新搭出那一页的小函数
    private readonly List<(string title, Func<Control> build, float w, float h)> windowStack = new();

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
        // 暗幕：只是把实验室压暗，不响应点击关闭（电脑开着就是开着）
        var scrim = new ColorRect
        {
            Color = new Color(0.04f, 0.05f, 0.07f, 0.68f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

        // ---- 显示器 ----
        monitorRoot = new Control { MouseFilter = MouseFilterEnum.Ignore };
        monitorRoot.AnchorLeft = 0.5f; monitorRoot.AnchorRight = 0.5f;
        monitorRoot.AnchorTop = 0.5f; monitorRoot.AnchorBottom = 0.5f;
        monitorRoot.OffsetLeft = -MonitorW / 2f; monitorRoot.OffsetRight = MonitorW / 2f;
        monitorRoot.OffsetTop = -MonitorH / 2f; monitorRoot.OffsetBottom = MonitorH / 2f;
        monitorRoot.PivotOffset = new Vector2(MonitorW / 2f, MonitorH / 2f);
        AddChild(monitorRoot);

        var bezel = new Panel();
        bezel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bezelStyle = new StyleBoxFlat
        {
            BgColor = BezelColor,
            BorderColor = BezelEdge,
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = 26,
            ShadowOffset = new Vector2(0, 12),
        };
        bezelStyle.SetCornerRadiusAll(20);
        bezel.AddThemeStyleboxOverride("panel", bezelStyle);
        bezel.MouseFilter = MouseFilterEnum.Ignore;
        monitorRoot.AddChild(bezel);

        // 屏幕本体
        screen = new ScreenView();
        screen.Position = new Vector2(BezelSide, BezelTop);
        screen.Size = new Vector2(ScreenW, ScreenH);
        screen.ClipContents = true;
        monitorRoot.AddChild(screen);

        // 桌面层（图标 / 便利贴 / 窗口 / 菜单 / 任务栏都在这层里）
        desktopLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        desktopLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        screen.AddChild(desktopLayer);
        BuildDesktop();
        BuildTaskbar();

        // 开始菜单（默认收着）
        BuildStartMenu();

        // 窗口层
        windowLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        windowLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        desktopLayer.AddChild(windowLayer);

        // 开机自检 / 黑屏
        bootView = new BootView { Name = "BootView" };
        bootView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bootView.Visible = false;
        screen.AddChild(bootView);

        offView = new OffView { Name = "OffView" };
        offView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        offView.Visible = false;
        screen.AddChild(offView);

        // ---- 下边框：品牌 + 电源灯 + 电源键 ----
        var brand = new Label { Text = ComputerData.Data.Brand, MouseFilter = MouseFilterEnum.Ignore };
        brand.AddThemeFontSizeOverride("font_size", 18);
        brand.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.63f));
        brand.Position = new Vector2(BezelSide + 6, ScreenH + (BezelTop + BezelBottom) / 2f - 16);
        monitorRoot.AddChild(brand);

        var ledLabel = new Label { Text = DataStore.Text("pc.led_label"), MouseFilter = MouseFilterEnum.Ignore };
        ledLabel.AddThemeFontSizeOverride("font_size", 16);
        ledLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.48f, 0.53f));
        ledLabel.Position = new Vector2(MonitorW - BezelSide - 150, ScreenH + (BezelTop + BezelBottom) / 2f - 14);
        monitorRoot.AddChild(ledLabel);

        powerLed = new PowerLed();
        powerLed.Position = new Vector2(MonitorW - BezelSide - 62, ScreenH + (BezelTop + BezelBottom) / 2f - 15);
        powerLed.Size = new Vector2(20, 20);
        powerLed.MouseFilter = MouseFilterEnum.Ignore;
        monitorRoot.AddChild(powerLed);

        var powerBtn = new PowerButton();
        powerBtn.Position = new Vector2(MonitorW - BezelSide - 44, ScreenH + (BezelTop + BezelBottom) / 2f - 22);
        powerBtn.Size = new Vector2(44, 44);
        powerBtn.TooltipText = DataStore.Text("pc.power_tip");
        powerBtn.Pressed += OnPowerPressed;
        monitorRoot.AddChild(powerBtn);
        UiSounds.Wire(powerBtn);

        // ---- 收起：起身离开（右下角小牌子） ----
        var leaveBtn = new Button
        {
            Text = DataStore.Text("pc.leave"),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = DataStore.Text("pc.leave_tip"),
        };
        leaveBtn.AnchorLeft = 1f; leaveBtn.AnchorRight = 1f;
        leaveBtn.AnchorTop = 1f; leaveBtn.AnchorBottom = 1f;
        leaveBtn.OffsetLeft = -216; leaveBtn.OffsetRight = -44;
        // 上移避开右下角全局「返回菜单」按钮（其占位 y: H-64..H-20）
        leaveBtn.OffsetTop = -132; leaveBtn.OffsetBottom = -80;
        leaveBtn.AddThemeFontSizeOverride("font_size", 20);
        leaveBtn.AddThemeColorOverride("font_color", new Color(0.92f, 0.93f, 0.95f));
        leaveBtn.AddThemeColorOverride("font_hover_color", Colors.White);
        leaveBtn.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0.16f, 0.18f, 0.22f, 0.85f), 12));
        leaveBtn.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.24f, 0.27f, 0.33f, 0.9f), 12));
        leaveBtn.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.12f, 0.13f, 0.16f, 0.95f), 12));
        leaveBtn.Pressed += Close;
        AddChild(leaveBtn);
        UiSounds.Wire(leaveBtn);

        // 左下角一行小字：明示按 Esc 的退路
        var leaveHint = new Label
        {
            Text = DataStore.Text("pc.leave_tip"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        leaveHint.AnchorLeft = 0f; leaveHint.AnchorRight = 0f;
        leaveHint.AnchorTop = 1f; leaveHint.AnchorBottom = 1f;
        leaveHint.OffsetLeft = 44; leaveHint.OffsetTop = -80; leaveHint.OffsetBottom = -46;
        leaveHint.AddThemeFontSizeOverride("font_size", 18);
        leaveHint.AddThemeColorOverride("font_color", new Color(0.68f, 0.71f, 0.76f));
        AddChild(leaveHint);

        // 屏幕画壁纸
        screen.QueueRedraw();
    }

    private static StyleBoxFlat RoundStyle(Color bg, int radius)
    {
        var st = new StyleBoxFlat { BgColor = bg };
        st.SetCornerRadiusAll(radius);
        return st;
    }

    // ---------- 桌面 ----------

    private void BuildDesktop()
    {
        var data = ComputerData.Data;
        int i = 0;
        foreach (var entry in data.Desktop)
        {
            var btn = new Button
            {
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                Position = new Vector2(18, 30 + i * 112),
                Size = new Vector2(104, 104),
            };
            btn.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0, 0, 0, 0), 10));
            btn.AddThemeStyleboxOverride("hover", RoundStyle(new Color(1f, 1f, 1f, 0.16f), 10));
            btn.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(1f, 1f, 1f, 0.26f), 10));
            var e = entry;
            btn.Pressed += () => OpenDesktopEntry(e);

            var icon = new ComputerIcon { Kind = entry.Icon, MouseFilter = MouseFilterEnum.Ignore };
            icon.Position = new Vector2(24, 4);
            icon.Size = new Vector2(56, 56);
            btn.AddChild(icon);

            var label = new Label
            {
                Text = entry.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.Position = new Vector2(0, 66);
            label.Size = new Vector2(104, 30);
            label.AddThemeFontSizeOverride("font_size", 17);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.99f, 0.96f));
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.55f));
            label.AddThemeConstantOverride("outline_size", 6);
            btn.AddChild(label);

            desktopLayer.AddChild(btn);
            i++;
        }

        // 便利贴：贴在屏幕右上方
        var sticky = new PanelContainer
        {
            Position = new Vector2(978, 64),
            Size = new Vector2(184, 172),
            Rotation = 0.045f,
            PivotOffset = new Vector2(92, 86),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var stickyStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.968f, 0.886f, 0.486f),
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowSize = 8,
            ShadowOffset = new Vector2(2, 4),
        };
        sticky.AddThemeStyleboxOverride("panel", stickyStyle);
        sticky.AddThemeConstantOverride("margin_left", 16);
        sticky.AddThemeConstantOverride("margin_right", 14);
        sticky.AddThemeConstantOverride("margin_top", 16);
        sticky.AddThemeConstantOverride("margin_bottom", 12);

        var stickyText = new Label
        {
            Text = data.StickyNote,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stickyText.AddThemeFontSizeOverride("font_size", 19);
        stickyText.AddThemeColorOverride("font_color", new Color(0.42f, 0.35f, 0.16f));
        stickyText.AddThemeConstantOverride("line_spacing", 6);
        sticky.AddChild(stickyText);
        desktopLayer.AddChild(sticky);
    }

    // ---------- 任务栏 ----------

    private void BuildTaskbar()
    {
        var bar = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        bar.AnchorTop = 1f; bar.AnchorBottom = 1f;
        bar.AnchorRight = 1f;
        bar.OffsetLeft = 0; bar.OffsetRight = 0;
        bar.OffsetTop = -TaskbarH; bar.OffsetBottom = 0;
        var barStyle = new StyleBoxFlat
        {
            BgColor = TaskbarColor,
            BorderColor = TaskbarEdge,
            BorderWidthTop = 2,
        };
        bar.AddThemeStyleboxOverride("panel", barStyle);
        desktopLayer.AddChild(bar);

        var startBtn = new Button
        {
            Text = DataStore.Text("pc.start"),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            Position = new Vector2(6, 6),
            Size = new Vector2(86, 30),
        };
        startBtn.AddThemeFontSizeOverride("font_size", 19);
        startBtn.AddThemeColorOverride("font_color", Colors.White);
        startBtn.AddThemeStyleboxOverride("normal", RoundStyle(StartGreen, 9));
        startBtn.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.30f, 0.70f, 0.34f), 9));
        startBtn.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.19f, 0.52f, 0.23f), 9));
        startBtn.Pressed += ToggleStartMenu;
        bar.AddChild(startBtn);
        UiSounds.Wire(startBtn);

        clockLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
        clockLabel.AnchorLeft = 1f; clockLabel.AnchorRight = 1f;
        clockLabel.OffsetLeft = -90; clockLabel.OffsetRight = -12;
        clockLabel.OffsetTop = 6; clockLabel.OffsetBottom = 34;
        clockLabel.HorizontalAlignment = HorizontalAlignment.Right;
        clockLabel.AddThemeFontSizeOverride("font_size", 18);
        clockLabel.AddThemeColorOverride("font_color", new Color(0.93f, 0.95f, 1f));
        bar.AddChild(clockLabel);
        RefreshClock(true);
    }

    private void RefreshClock(bool force = false)
    {
        string now = StoryClock.ClockText();
        if (force || now != lastClock)
        {
            lastClock = now;
            clockLabel.Text = now;
        }
    }

    private void BuildStartMenu()
    {
        startMenu = new Panel
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
            Position = new Vector2(6, ScreenH - TaskbarH - 128),
            Size = new Vector2(190, 124),
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.93f, 0.95f),
            BorderColor = new Color(0.42f, 0.46f, 0.55f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowSize = 10,
            ShadowOffset = new Vector2(3, 4),
        };
        style.SetCornerRadiusAll(8);
        startMenu.AddThemeStyleboxOverride("panel", style);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 8; col.OffsetRight = -8; col.OffsetTop = 8; col.OffsetBottom = -8;
        col.AddThemeConstantOverride("separation", 4);
        startMenu.AddChild(col);

        col.AddChild(MakeMenuButton(DataStore.Text("pc.menu_shutdown"), () =>
        {
            HideStartMenu();
            BeginShutdown(false);
        }));
        col.AddChild(MakeMenuButton(DataStore.Text("pc.menu_restart"), () =>
        {
            HideStartMenu();
            BeginShutdown(true);
        }));
        desktopLayer.AddChild(startMenu);
    }

    private Button MakeMenuButton(string text, Action onPressed)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 44),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 20);
        btn.AddThemeColorOverride("font_color", InkDark);
        btn.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0, 0, 0, 0), 6));
        btn.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.12f, 0.30f, 0.72f, 0.90f), 6));
        btn.AddThemeColorOverride("font_hover_color", Colors.White);
        btn.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.10f, 0.24f, 0.58f), 6));
        btn.Pressed += onPressed;
        UiSounds.Wire(btn);
        return btn;
    }

    private void ToggleStartMenu()
    {
        if (state != StOn)
            return;
        startMenu.Visible = !startMenu.Visible;
    }

    private void HideStartMenu() => startMenu.Visible = false;

    // ==================== 开关机 ====================

    private void OnPowerPressed()
    {
        switch (state)
        {
            case StOff:
                StartBoot();
                break;
            case StBooting:
                bootView.Skip(); // 再按一下电源键也算跳过
                break;
        }
    }

    private void StartBoot()
    {
        state = StBooting;
        offView.Visible = false;
        HideStartMenu();
        CloseWindowImmediate();
        bootView.Begin(ComputerData.Data.BootLines, OnBootFinished);
        powerLed.SetOn(false, true); // 开机过程中先亮一点点
    }

    private void OnBootFinished()
    {
        state = StOn;
        bootView.Visible = false;
        powerLed.SetOn(true);
        RefreshClock(true);
    }

    private void BeginShutdown(bool restart)
    {
        if (state != StOn)
            return;
        state = StShutdown;
        restartAfterShutdown = restart;
        HideStartMenu();
        CloseWindowImmediate();
        offView.ShowShutdown(restart ? DataStore.Text("pc.restarting") : DataStore.Text("pc.shutting_down"));
        powerLed.SetOn(false);

        GetTree().CreateTimer(1.6).Timeout += () =>
        {
            if (!IsInsideTree())
                return;
            if (restartAfterShutdown)
            {
                StartBoot();
            }
            else
            {
                state = StOff;
                offView.ShowOff(DataStore.Text("pc.shutdown_done"));
            }
        };
    }

    // ==================== 窗口系统 ====================

    private void OpenDesktopEntry(ComputerData.DesktopEntry entry)
    {
        if (state != StOn)
            return;
        HideStartMenu();

        switch (entry.Type)
        {
            case "mycomputer":
                PushWindow(entry.Name, BuildMyComputerView, 720, 470);
                break;
            case "folder":
                string group = entry.Group;
                PushWindow(GroupTitle(group), () => BuildFileListView(group), 720, 560);
                break;
            case "mail":
                PushWindow(DataStore.Text("pc.mail_title"), BuildMailListView, 760, 600);
                break;
            case "file":
                var f = ComputerData.File(entry.FileId);
                if (f != null)
                    PushWindow(f.Name, () => BuildViewerView(f), 760, 640);
                break;
        }
    }

    private string GroupTitle(string group)
    {
        return ComputerData.Data.GroupTitles.TryGetValue(group, out var t) ? t : group;
    }

    /// <summary>推一页窗口（同类型重开时直接替换，回退栈记住来路）</summary>
    private void PushWindow(string title, Func<Control> build, float w, float h, bool keepStack = true)
    {
        if (!keepStack)
            windowStack.Clear();
        windowStack.Add((title, build, w, h));
        ShowTopWindow();
    }

    private void ShowTopWindow()
    {
        if (windowStack.Count == 0)
        {
            CloseWindowImmediate();
            return;
        }

        var (title, build, w, h) = windowStack[^1];
        CloseWindowImmediate();

        var win = new Panel
        {
            Position = new Vector2(WinX, WinY),
            Size = new Vector2(w, h),
        };
        var winStyle = new StyleBoxFlat
        {
            BgColor = WinBody,
            BorderColor = new Color(0.30f, 0.34f, 0.42f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            ShadowColor = new Color(0f, 0f, 0f, 0.40f),
            ShadowSize = 14,
            ShadowOffset = new Vector2(4, 6),
        };
        winStyle.SetCornerRadiusAll(8);
        win.AddThemeStyleboxOverride("panel", winStyle);
        windowLayer.AddChild(win);

        // 标题栏
        var titleBar = new Panel { MouseFilter = MouseFilterEnum.Stop };
        titleBar.Position = Vector2.Zero;
        titleBar.Size = new Vector2(w, TitleH);
        var tbStyle = new StyleBoxFlat { BgColor = TitleBlue };
        tbStyle.CornerRadiusTopLeft = 8;
        tbStyle.CornerRadiusTopRight = 8;
        titleBar.AddThemeStyleboxOverride("panel", tbStyle);
        win.AddChild(titleBar);

        var titleLabel = new Label
        {
            Text = title,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleLabel.Position = new Vector2(14, 0);
        titleLabel.Size = new Vector2(w - 60, TitleH);
        titleLabel.AddThemeFontSizeOverride("font_size", 21);
        titleLabel.AddThemeColorOverride("font_color", Colors.White);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button
        {
            Text = "✕",
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = DataStore.Text("pc.close_tip"),
            Position = new Vector2(w - 36, 6),
            Size = new Vector2(28, 28),
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.AddThemeColorOverride("font_color", Colors.White);
        closeBtn.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0.55f, 0.20f, 0.18f), 6));
        closeBtn.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.85f, 0.28f, 0.24f), 6));
        closeBtn.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.42f, 0.14f, 0.12f), 6));
        closeBtn.Pressed += () =>
        {
            windowStack.Clear();
            ShowTopWindow();
        };
        titleBar.AddChild(closeBtn);
        UiSounds.Wire(closeBtn);

        // 内容区（标题栏往下）
        var body = build();
        var bodyHost = new Control { MouseFilter = MouseFilterEnum.Ignore };
        bodyHost.Position = new Vector2(0, TitleH);
        bodyHost.Size = new Vector2(w, h - TitleH);
        win.AddChild(bodyHost);
        bodyHost.AddChild(body);

        // 出现动画
        win.PivotOffset = new Vector2(w / 2f, h / 2f);
        win.Scale = new Vector2(0.97f, 0.97f);
        win.Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(win, "scale", Vector2.One, 0.14).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(win, "modulate:a", 1f, 0.14);
    }

    private void CloseWindowImmediate()
    {
        foreach (var child in windowLayer.GetChildren())
        {
            windowLayer.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Esc / 返回按钮：退一步（先收菜单 → 再退窗口层级 → 最后起身离开）</summary>
    private void GoBackOneStep()
    {
        if (startMenu.Visible)
        {
            HideStartMenu();
            return;
        }
        if (windowStack.Count > 0)
        {
            windowStack.RemoveAt(windowStack.Count - 1);
            ShowTopWindow();
            return;
        }
        Close();
    }

    // ==================== 各页内容 ====================

    /// <summary>我的电脑：几个盘 + 一句备注</summary>
    private Control BuildMyComputerView()
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 26; col.OffsetRight = -26; col.OffsetTop = 20; col.OffsetBottom = -20;
        col.AddThemeConstantOverride("separation", 14);
        root.AddChild(col);

        foreach (var d in ComputerData.Data.Drives)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 14);
            col.AddChild(row);

            var icon = new ComputerIcon { Kind = "drive", MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(44, 44) };
            row.AddChild(icon);

            var texts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            texts.AddThemeConstantOverride("separation", 2);
            row.AddChild(texts);

            var name = new Label { Text = d.Name, MouseFilter = MouseFilterEnum.Ignore };
            name.AddThemeFontSizeOverride("font_size", 22);
            name.AddThemeColorOverride("font_color", InkDark);
            texts.AddChild(name);

            var bar = new DriveBar
            {
                Percent = d.Percent,
                Warn = d.Warn,
                CustomMinimumSize = new Vector2(0, 14),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            texts.AddChild(bar);

            var hint = new Label { Text = d.Hint, MouseFilter = MouseFilterEnum.Ignore };
            hint.AddThemeFontSizeOverride("font_size", 18);
            hint.AddThemeColorOverride("font_color", InkMid);
            texts.AddChild(hint);
        }

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(spacer);

        var spec = new Label { Text = ComputerData.Data.SpecLine, MouseFilter = MouseFilterEnum.Ignore };
        spec.AddThemeFontSizeOverride("font_size", 18);
        spec.AddThemeColorOverride("font_color", InkMid);
        col.AddChild(spec);

        col.AddChild(MakeNoteLabel(ComputerData.Data.MyComputerNote));
        return root;
    }

    /// <summary>文件夹页：一排文件行，点开有内容</summary>
    private Control BuildFileListView(string group)
    {
        var files = ComputerData.FilesIn(group);
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer { MouseFilter = MouseFilterEnum.Stop };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 16; scroll.OffsetRight = -16; scroll.OffsetTop = 12; scroll.OffsetBottom = -12;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        root.AddChild(scroll);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        foreach (var f in files)
        {
            var file = f;
            var row = new Button
            {
                CustomMinimumSize = new Vector2(0, 52),
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            row.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0, 0, 0, 0), 8));
            row.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.13f, 0.31f, 0.72f, 0.14f), 8));
            row.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.13f, 0.31f, 0.72f, 0.24f), 8));
            row.Pressed += () => PushWindow(file.Name, () => BuildViewerView(file), 760, 640);
            UiSounds.Wire(row);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            line.OffsetLeft = 10; line.OffsetRight = -10;
            line.AddThemeConstantOverride("separation", 12);
            row.AddChild(line);

            var icon = new ComputerIcon
            {
                Kind = IconForKind(file.Kind),
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(38, 38),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            line.AddChild(icon);

            var name = new Label
            {
                Text = file.Name,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            name.AddThemeFontSizeOverride("font_size", 21);
            name.AddThemeColorOverride("font_color", InkDark);
            line.AddChild(name);

            col.AddChild(row);
        }

        return root;
    }

    private static string IconForKind(string kind) => kind switch
    {
        "doc" => "doc",
        "chart" => "chart",
        "table" => "table",
        "photo" => "photo",
        "audio" => "audio",
        _ => "text",
    };

    /// <summary>邮件列表</summary>
    private Control BuildMailListView()
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer { MouseFilter = MouseFilterEnum.Stop };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 16; scroll.OffsetRight = -16; scroll.OffsetTop = 12; scroll.OffsetBottom = -12;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        root.AddChild(scroll);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        var mails = ComputerData.Data.Mail;
        for (int i = 0; i < mails.Count; i++)
        {
            var mail = mails[i];
            var row = new Button
            {
                CustomMinimumSize = new Vector2(0, 66),
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            row.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0, 0, 0, 0), 8));
            row.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.13f, 0.31f, 0.72f, 0.14f), 8));
            row.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.13f, 0.31f, 0.72f, 0.24f), 8));
            row.Pressed += () =>
            {
                mail.Unread = false; // 看过就不新了
                PushWindow(mail.Subject, () => BuildMailDetailView(mail), 760, 600);
            };
            UiSounds.Wire(row);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            line.OffsetLeft = 10; line.OffsetRight = -10;
            line.AddThemeConstantOverride("separation", 12);
            row.AddChild(line);

            if (mail.Unread)
            {
                var dot = new UnreadDot { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                line.AddChild(dot);
            }
            else
            {
                line.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0), MouseFilter = MouseFilterEnum.Ignore });
            }

            var icon = new ComputerIcon { Kind = "mail", MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(38, 38), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            line.AddChild(icon);

            var texts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            texts.AddThemeConstantOverride("separation", 2);
            line.AddChild(texts);

            var subject = new Label { Text = mail.Subject, MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            subject.AddThemeFontSizeOverride("font_size", 21);
            subject.AddThemeColorOverride("font_color", InkDark);
            texts.AddChild(subject);

            var meta = new Label { Text = $"{mail.From} · {mail.Time}", MouseFilter = MouseFilterEnum.Ignore };
            meta.AddThemeFontSizeOverride("font_size", 17);
            meta.AddThemeColorOverride("font_color", InkMid);
            texts.AddChild(meta);

            col.AddChild(row);
        }

        return root;
    }

    private Control BuildMailDetailView(ComputerData.MailEntry mail)
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 30; col.OffsetRight = -30; col.OffsetTop = 22; col.OffsetBottom = -64;
        col.AddThemeConstantOverride("separation", 10);
        root.AddChild(col);

        var subject = new Label { Text = mail.Subject, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        subject.AddThemeFontSizeOverride("font_size", 28);
        subject.AddThemeColorOverride("font_color", InkDark);
        col.AddChild(subject);

        var meta = new Label { Text = $"{mail.From} · {mail.Time}", MouseFilter = MouseFilterEnum.Ignore };
        meta.AddThemeFontSizeOverride("font_size", 18);
        meta.AddThemeColorOverride("font_color", InkMid);
        col.AddChild(meta);

        var sep = new ColorRect { Color = WinLine, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(sep);

        foreach (var line in mail.Body)
        {
            var l = new Label { Text = line, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", 21);
            l.AddThemeColorOverride("font_color", InkDark);
            l.AddThemeConstantOverride("line_spacing", 6);
            col.AddChild(l);
        }

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (!string.IsNullOrEmpty(mail.Note))
            col.AddChild(MakeNoteLabel(mail.Note));

        AddBackButton(root);
        return root;
    }

    /// <summary>文件查看器：按 kind 画不同内容</summary>
    private Control BuildViewerView(ComputerData.FileEntry f)
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.OffsetTop = 16; center.OffsetBottom = -60;
        root.AddChild(center);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 12);
        center.AddChild(col);

        switch (f.Kind)
        {
            case "doc":
                col.AddChild(BuildDocPage(f));
                break;
            case "chart":
                col.AddChild(BuildXrdChart(f));
                break;
            case "table":
                col.AddChild(BuildTableView(f));
                break;
            case "photo":
                col.AddChild(BuildPhotoView(f));
                break;
            case "audio":
                col.AddChild(BuildAudioView(f));
                break;
            default:
                col.AddChild(BuildTextView(f));
                break;
        }

        if (!string.IsNullOrEmpty(f.Note))
        {
            var note = MakeNoteLabel(f.Note);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            // 【坑·第十五轮回归】这里原来还设了 SizeFlagsHorizontal = ShrinkCenter，
            // 想让灰字居中——但纯中文 Label 开了自动换行后"最小宽度≈一个字"，
            // ShrinkCenter 会把它饿成一条竖排（截图里像条毛毛虫）。留 Fill，
            // 让注释吃满内容列宽、文字在列内居中即可。
            col.AddChild(note);
        }

        AddBackButton(root);
        return root;
    }

    private Control BuildDocPage(ComputerData.FileEntry f)
    {
        var page = new Panel { CustomMinimumSize = new Vector2(600, 380), MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = new Color(0.80f, 0.82f, 0.86f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ShadowColor = new Color(0f, 0f, 0f, 0.18f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(2, 3),
        };
        page.AddThemeStyleboxOverride("panel", style);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 40; col.OffsetRight = -40; col.OffsetTop = 30; col.OffsetBottom = -26;
        col.AddThemeConstantOverride("separation", 14);
        page.AddChild(col);

        var title = new Label
        {
            Text = f.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 27);
        title.AddThemeColorOverride("font_color", InkDark);
        title.AddThemeConstantOverride("line_spacing", 8);
        col.AddChild(title);

        foreach (var line in f.Lines)
        {
            var l = new Label { Text = line, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 20);
            l.AddThemeColorOverride("font_color", InkMid);
            col.AddChild(l);
        }

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        if (!string.IsNullOrEmpty(f.Annotation))
        {
            var anno = new Label
            {
                Text = f.Annotation,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            anno.AddThemeFontSizeOverride("font_size", 21);
            anno.AddThemeColorOverride("font_color", InkRed);
            col.AddChild(anno);

            var sign = new Label { Text = DataStore.Text("pc.mentor_sign"), HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore };
            sign.AddThemeFontSizeOverride("font_size", 18);
            sign.AddThemeColorOverride("font_color", InkRed);
            col.AddChild(sign);
        }

        return page;
    }

    private Control BuildXrdChart(ComputerData.FileEntry f)
    {
        var box = new Panel { CustomMinimumSize = new Vector2(620, 380), MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.10f, 0.11f, 0.13f) };
        box.AddThemeStyleboxOverride("panel", style);

        var chart = new XrdChartView { MouseFilter = MouseFilterEnum.Ignore };
        chart.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        chart.OffsetLeft = 18; chart.OffsetRight = -18; chart.OffsetTop = 18; chart.OffsetBottom = -18;
        box.AddChild(chart);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 6);
        foreach (var line in f.Lines)
        {
            var l = new Label { Text = line, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 19);
            l.AddThemeColorOverride("font_color", InkMid);
            col.AddChild(l);
        }

        var wrap = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        wrap.AddThemeConstantOverride("separation", 10);
        // 标题挂在图上方（第十五轮回归补的：原来图谱页缺一行标题，只有文件名在窗框上）
        var title = new Label { Text = f.Title, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", InkDark);
        wrap.AddChild(title);
        wrap.AddChild(box);
        wrap.AddChild(col);
        return wrap;
    }

    private Control BuildTableView(ComputerData.FileEntry f)
    {
        var grid = new GridContainer { Columns = Mathf.Max(1, f.Rows.Count > 0 ? f.Rows[0].Count : 1), MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 0);
        grid.AddThemeConstantOverride("v_separation", 0);

        for (int r = 0; r < f.Rows.Count; r++)
        {
            for (int c = 0; c < f.Rows[r].Count; c++)
            {
                var cell = new Panel { CustomMinimumSize = new Vector2(150, 46), MouseFilter = MouseFilterEnum.Ignore };
                var cellStyle = new StyleBoxFlat
                {
                    BgColor = r == 0 ? new Color(0.16f, 0.33f, 0.58f) : (r % 2 == 0 ? new Color(1f, 1f, 1f) : new Color(0.93f, 0.95f, 0.97f)),
                    BorderColor = new Color(0.75f, 0.78f, 0.82f),
                    BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
                };
                cell.AddThemeStyleboxOverride("panel", cellStyle);

                var l = new Label
                {
                    Text = f.Rows[r][c],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                l.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                l.AddThemeFontSizeOverride("font_size", 20);
                l.AddThemeColorOverride("font_color", r == 0 ? Colors.White : InkDark);
                cell.AddChild(l);
                grid.AddChild(cell);
            }
        }

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 12);
        var title = new Label { Text = f.Title, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", InkDark);
        col.AddChild(title);
        col.AddChild(grid);
        return col;
    }

    private Control BuildPhotoView(ComputerData.FileEntry f)
    {
        var box = new Panel { CustomMinimumSize = new Vector2(620, 330), MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        var style = new StyleBoxFlat { BgColor = new Color(0.09f, 0.10f, 0.12f) };
        box.AddThemeStyleboxOverride("panel", style);

        var view = new SemBlobView { MouseFilter = MouseFilterEnum.Ignore };
        view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.AddChild(view);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 6);
        foreach (var line in f.Lines)
        {
            var l = new Label { Text = line, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 19);
            l.AddThemeColorOverride("font_color", InkMid);
            col.AddChild(l);
        }

        var wrap = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        wrap.AddThemeConstantOverride("separation", 10);
        wrap.AddChild(box);
        wrap.AddChild(col);
        return wrap;
    }

    private Control BuildAudioView(ComputerData.FileEntry f)
    {
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 14);
        col.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        var cover = new Panel { CustomMinimumSize = new Vector2(220, 220), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var coverStyle = new StyleBoxFlat { BgColor = new Color(0.36f, 0.28f, 0.62f) };
        coverStyle.SetCornerRadiusAll(20);
        cover.AddThemeStyleboxOverride("panel", coverStyle);
        var glyph = new Label
        {
            Text = "♪",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        glyph.AddThemeFontSizeOverride("font_size", 92);
        glyph.AddThemeColorOverride("font_color", new Color(0.94f, 0.92f, 1f));
        cover.AddChild(glyph);
        col.AddChild(cover);

        var title = new Label { Text = f.Title, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 30);
        col.AddChild(title);

        var badge = new Label { Text = DataStore.Text("pc.playing"), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        badge.AddThemeFontSizeOverride("font_size", 18);
        badge.AddThemeColorOverride("font_color", new Color(0.44f, 0.30f, 0.80f));
        col.AddChild(badge);

        foreach (var line in f.Lines)
        {
            var l = new Label { Text = line, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 19);
            l.AddThemeColorOverride("font_color", InkMid);
            col.AddChild(l);
        }

        return col;
    }

    private Control BuildTextView(ComputerData.FileEntry f)
    {
        var page = new Panel { CustomMinimumSize = new Vector2(560, 330), MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(1f, 0.995f, 0.97f), BorderColor = new Color(0.82f, 0.80f, 0.72f), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
        page.AddThemeStyleboxOverride("panel", style);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 30; col.OffsetRight = -30; col.OffsetTop = 24; col.OffsetBottom = -24;
        col.AddThemeConstantOverride("separation", 10);
        page.AddChild(col);

        foreach (var line in f.Lines)
        {
            var l = new Label { Text = line, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 21);
            l.AddThemeColorOverride("font_color", new Color(0.24f, 0.26f, 0.30f));
            col.AddChild(l);
        }

        return page;
    }

    private Label MakeNoteLabel(string text)
    {
        var note = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        note.AddThemeFontSizeOverride("font_size", 19);
        note.AddThemeColorOverride("font_color", NoteGrey);
        return note;
    }

    private void AddBackButton(Control root)
    {
        var back = new Button
        {
            Text = DataStore.Text("pc.back"),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        back.AnchorTop = 1f; back.AnchorBottom = 1f;
        back.OffsetLeft = 20; back.OffsetRight = 130;
        back.OffsetTop = -50; back.OffsetBottom = -12;
        back.AddThemeFontSizeOverride("font_size", 19);
        back.AddThemeColorOverride("font_color", InkDark);
        back.AddThemeStyleboxOverride("normal", RoundStyle(new Color(0.82f, 0.85f, 0.89f), 8));
        back.AddThemeStyleboxOverride("hover", RoundStyle(new Color(0.88f, 0.90f, 0.94f), 8));
        back.AddThemeStyleboxOverride("pressed", RoundStyle(new Color(0.72f, 0.76f, 0.82f), 8));
        back.Pressed += GoBackOneStep;
        root.AddChild(back);
        UiSounds.Wire(back);
    }

    // ==================== 开合 / 输入 ====================

    /// <summary>打开电脑（挂进场景树后调用）</summary>
    public void Open()
    {
        if (opened)
            return;
        opened = true;

        state = StOn; // 直接就是开着的桌面（熄屏状态可以自己关机玩出来）
        ApplyInitialState();

        suppressing = true;
        DialogueManager.Instance?.SetUiSuppressed(true);

        Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 1f, 0.22).SetEase(Tween.EaseType.Out);
    }

    private void ApplyInitialState()
    {
        // 一进来就是亮着的桌面；黑屏/自检只在玩家自己关机再开机时出现
        offView.Visible = false;
        bootView.Visible = false;
        powerLed.SetOn(true);
        windowStack.Clear();
        CloseWindowImmediate();
    }

    /// <summary>收起（fade 后销毁，并通知外面）</summary>
    public void Close()
    {
        if (closing)
            return;
        closing = true;

        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, 0.18).SetEase(Tween.EaseType.In);
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
        // 兜底：没走 Close 就被销毁（切场景）时别把背景输入锁死
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
            GoBackOneStep();
        }
    }

    public override void _Process(double delta)
    {
        RefreshClock();
    }

    /// <summary>屏幕比设计尺寸还小时整体等比缩小（手机框那套逻辑）</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationResized || what == NotificationReady)
        {
            if (monitorRoot == null || !IsInsideTree())
                return;
            var vp = GetViewportRect().Size;
            float s = Mathf.Min(1f, Mathf.Min((vp.X - 120f) / MonitorW, (vp.Y - 60f) / MonitorH));
            monitorRoot.Scale = new Vector2(s, s);
        }
    }

    // ==================== 内部小部件 ====================

    /// <summary>屏幕：画壁纸 + 接收"点空白处收开始菜单"</summary>
    private partial class ScreenView : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
        }

        public override void _Draw()
        {
            // 深蓝渐变壁纸（上浅下深）
            var top = new Color(0.23f, 0.38f, 0.66f);
            var bottom = new Color(0.10f, 0.18f, 0.38f);
            var pts = new Vector2[] { Vector2.Zero, new(Size.X, 0), new(Size.X, Size.Y), new(0, Size.Y) };
            DrawPolygon(pts, new[] { top, top, bottom, bottom });

            // 底部一条微亮的"地平线"，让纯渐变不那么空
            var bandY = Size.Y * 0.74f;
            DrawRect(new Rect2(0, bandY, Size.X, 2f), new Color(1f, 1f, 1f, 0.06f));
            DrawRect(new Rect2(0, bandY + 2f, Size.X, Size.Y - bandY - 2f), new Color(0.06f, 0.11f, 0.26f, 0.55f));
        }
    }

    /// <summary>开机自检画面：一行行冒字 + 光标闪烁 + 点击跳过</summary>
    private partial class BootView : Control
    {
        private VBoxContainer column;
        private Label lastLine;
        private string lastBase = "";
        private bool blinking;
        private int lineIndex;
        private List<string> lines = new();
        private Action onFinished;
        private double timer;
        private bool finishing;
        private Label skipHint;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;

            var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f), MouseFilter = MouseFilterEnum.Ignore };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.Position = new Vector2(56, 46);
            column.Size = new Vector2(900, 600);
            column.AddThemeConstantOverride("separation", 9);
            AddChild(column);

            skipHint = new Label { Text = DataStore.Text("pc.boot_skip"), MouseFilter = MouseFilterEnum.Ignore };
            skipHint.AnchorLeft = 1f; skipHint.AnchorTop = 1f; skipHint.AnchorRight = 1f; skipHint.AnchorBottom = 1f;
            skipHint.OffsetLeft = -260; skipHint.OffsetRight = -24; skipHint.OffsetTop = -44; skipHint.OffsetBottom = -16;
            skipHint.HorizontalAlignment = HorizontalAlignment.Right;
            skipHint.AddThemeFontSizeOverride("font_size", 17);
            skipHint.AddThemeColorOverride("font_color", new Color(0.42f, 0.46f, 0.42f));
            AddChild(skipHint);

            GuiInput += @event =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    AcceptEvent();
                    Skip();
                }
            };
        }

        public void Begin(List<string> bootLines, Action finished)
        {
            lines = bootLines ?? new List<string>();
            onFinished = finished;
            lineIndex = 0;
            timer = 0;
            finishing = false;
            lastLine = null;
            foreach (var child in column.GetChildren())
            {
                column.RemoveChild(child);
                child.QueueFree();
            }
            Visible = true;
        }

        public void Skip()
        {
            if (finishing)
                return;
            while (lineIndex < lines.Count)
                AppendLine(lines[lineIndex++]);
            Finish();
        }

        private void AppendLine(string text)
        {
            lastLine = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
            lastLine.AddThemeFontSizeOverride("font_size", 21);
            lastLine.AddThemeColorOverride("font_color", new Color(0.74f, 0.82f, 0.74f));
            column.AddChild(lastLine);
            lastBase = text;
        }

        private void Finish()
        {
            finishing = true;
            var tw = CreateTween();
            tw.TweenProperty(this, "modulate:a", 0f, 0.22);
            tw.TweenCallback(Callable.From(() =>
            {
                Visible = false;
                Modulate = Colors.White;
                onFinished?.Invoke();
            }));
        }

        public override void _Process(double delta)
        {
            if (finishing)
                return;

            if (lastLine != null)
            {
                blinking = Mathf.PosMod(Time.GetTicksMsec() / 380, 2) == 0;
                lastLine.Text = blinking ? lastBase + " ▊" : lastBase;
            }

            if (lineIndex >= lines.Count)
            {
                timer += delta;
                if (timer > 0.5)
                    Finish();
                return;
            }

            timer += delta;
            if (timer > 0.34)
            {
                timer = 0;
                AppendLine(lines[lineIndex++]);
            }
        }
    }

    /// <summary>关机/黑屏画面</summary>
    private partial class OffView : Control
    {
        private Label bigLabel;
        private Label hintLabel;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;

            var bg = new ColorRect { Color = new Color(0.015f, 0.015f, 0.025f), MouseFilter = MouseFilterEnum.Ignore };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            bigLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bigLabel.AnchorLeft = 0f; bigLabel.AnchorRight = 1f;
            bigLabel.AnchorTop = 0.5f; bigLabel.AnchorBottom = 0.5f;
            bigLabel.OffsetTop = -56; bigLabel.OffsetBottom = 4;
            bigLabel.AddThemeFontSizeOverride("font_size", 26);
            bigLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.70f, 0.66f));
            AddChild(bigLabel);

            hintLabel = new Label
            {
                Text = DataStore.Text("pc.power_hint"),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            hintLabel.AnchorLeft = 0f; hintLabel.AnchorRight = 1f;
            hintLabel.AnchorTop = 0.5f; hintLabel.AnchorBottom = 0.5f;
            hintLabel.OffsetTop = 18; hintLabel.OffsetBottom = 58;
            hintLabel.AddThemeFontSizeOverride("font_size", 19);
            hintLabel.AddThemeColorOverride("font_color", new Color(0.36f, 0.42f, 0.40f));
            AddChild(hintLabel);
        }

        public void ShowOff(string message)
        {
            bigLabel.Text = message;
            hintLabel.Visible = true;
            Visible = true;
            Modulate = Colors.White;
        }

        public void ShowShutdown(string message)
        {
            bigLabel.Text = message;
            hintLabel.Visible = false;
            Visible = true;
            Modulate = Colors.White;
        }
    }

    /// <summary>电源指示灯</summary>
    private partial class PowerLed : Control
    {
        private bool on;
        private bool dim;

        public void SetOn(bool value, bool dimShade = false)
        {
            on = value;
            dim = dimShade;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var c = Size / 2f;
            float r = Mathf.Min(Size.X, Size.Y) / 2f - 1;
            Color color = on
                ? new Color(0.36f, 0.92f, 0.44f)
                : dim ? new Color(0.45f, 0.42f, 0.20f) : new Color(0.30f, 0.14f, 0.14f);
            if (on)
                DrawCircle(c, r + 3f, new Color(0.36f, 0.92f, 0.44f, 0.28f));
            DrawCircle(c, r, color);
        }
    }

    /// <summary>电源键（圆钮 + ⏻ 符号）</summary>
    private partial class PowerButton : Button
    {
        public override void _Ready()
        {
            FocusMode = FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            AddThemeStyleboxOverride("normal", FlatStyle(new Color(0.18f, 0.20f, 0.24f)));
            AddThemeStyleboxOverride("hover", FlatStyle(new Color(0.26f, 0.29f, 0.35f)));
            AddThemeStyleboxOverride("pressed", FlatStyle(new Color(0.12f, 0.14f, 0.17f)));
        }

        private static StyleBoxFlat FlatStyle(Color bg)
        {
            var st = new StyleBoxFlat { BgColor = bg };
            st.SetCornerRadiusAll(22);
            return st;
        }

        public override void _Draw()
        {
            var c = Size / 2f;
            float r = Mathf.Min(Size.X, Size.Y) / 2f - 9f;
            var color = new Color(0.78f, 0.82f, 0.88f);
            DrawArc(c, r, -Mathf.Pi / 2f + 0.6f, Mathf.Pi * 1.5f - 0.6f, 40, color, 3.2f, true);
            DrawLine(c + new Vector2(0, -r - 3f), c + new Vector2(0, r * 0.15f), color, 3.2f);
        }
    }

    /// <summary>桌面小图标：纯矢量画，不依赖素材</summary>
    private partial class ComputerIcon : Control
    {
        public string Kind = "folder";

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            var w = Size.X;
            var h = Size.Y;
            switch (Kind)
            {
                case "computer":
                {
                    var body = new StyleBoxFlat { BgColor = new Color(0.32f, 0.36f, 0.44f) };
                    body.SetCornerRadiusAll(6);
                    DrawStyleBox(body, new Rect2(w * 0.04f, h * 0.02f, w * 0.92f, h * 0.66f));
                    DrawRect(new Rect2(w * 0.10f, h * 0.08f, w * 0.80f, h * 0.52f), new Color(0.42f, 0.62f, 0.90f));
                    DrawRect(new Rect2(w * 0.10f, h * 0.08f, w * 0.80f, h * 0.16f), new Color(0.58f, 0.74f, 0.96f));
                    DrawRect(new Rect2(w * 0.42f, h * 0.68f, w * 0.16f, h * 0.14f), new Color(0.32f, 0.36f, 0.44f));
                    DrawStyleBox(body, new Rect2(w * 0.22f, h * 0.82f, w * 0.56f, h * 0.10f));
                    break;
                }
                case "folder":
                {
                    var tab = new StyleBoxFlat { BgColor = new Color(0.86f, 0.66f, 0.26f) };
                    tab.SetCornerRadiusAll(4);
                    DrawStyleBox(tab, new Rect2(w * 0.06f, h * 0.14f, w * 0.42f, h * 0.20f));
                    var body = new StyleBoxFlat { BgColor = new Color(0.94f, 0.76f, 0.34f), BorderColor = new Color(0.72f, 0.55f, 0.20f), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
                    body.SetCornerRadiusAll(5);
                    DrawStyleBox(body, new Rect2(w * 0.04f, h * 0.26f, w * 0.92f, h * 0.58f));
                    break;
                }
                case "mail":
                {
                    var body = new StyleBoxFlat { BgColor = new Color(0.97f, 0.97f, 0.99f), BorderColor = new Color(0.30f, 0.48f, 0.85f), BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2 };
                    body.SetCornerRadiusAll(5);
                    DrawStyleBox(body, new Rect2(w * 0.06f, h * 0.20f, w * 0.88f, h * 0.60f));
                    var ink = new Color(0.30f, 0.48f, 0.85f);
                    DrawPolyline(new[] { new Vector2(w * 0.08f, h * 0.24f), new Vector2(w * 0.5f, h * 0.52f), new Vector2(w * 0.92f, h * 0.24f) }, ink, 2.4f, true);
                    break;
                }
                case "bin":
                {
                    var body = new StyleBoxFlat { BgColor = new Color(0.55f, 0.62f, 0.72f) };
                    body.SetCornerRadiusAll(4);
                    DrawPolygon(new[]
                    {
                        new Vector2(w * 0.26f, h * 0.32f),
                        new Vector2(w * 0.74f, h * 0.32f),
                        new Vector2(w * 0.66f, h * 0.92f),
                        new Vector2(w * 0.34f, h * 0.92f),
                    }, new[] { new Color(0.55f, 0.62f, 0.72f), new Color(0.55f, 0.62f, 0.72f), new Color(0.44f, 0.50f, 0.60f), new Color(0.44f, 0.50f, 0.60f) });
                    DrawStyleBox(body, new Rect2(w * 0.18f, h * 0.20f, w * 0.64f, h * 0.12f));
                    DrawRect(new Rect2(w * 0.42f, h * 0.13f, w * 0.16f, h * 0.07f), new Color(0.44f, 0.50f, 0.60f));
                    break;
                }
                case "audio":
                {
                    var tile = new StyleBoxFlat { BgColor = new Color(0.42f, 0.32f, 0.72f) };
                    tile.SetCornerRadiusAll(9);
                    DrawStyleBox(tile, new Rect2(w * 0.10f, h * 0.10f, w * 0.80f, h * 0.80f));
                    var font = GetThemeDefaultFont();
                    DrawString(font, new Vector2(0, h * 0.66f), "♪", HorizontalAlignment.Center, w, 34, new Color(1f, 1f, 1f, 0.95f));
                    break;
                }
                case "drive":
                {
                    var body = new StyleBoxFlat { BgColor = new Color(0.72f, 0.76f, 0.82f), BorderColor = new Color(0.52f, 0.56f, 0.62f), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
                    body.SetCornerRadiusAll(5);
                    DrawStyleBox(body, new Rect2(w * 0.04f, h * 0.22f, w * 0.92f, h * 0.56f));
                    DrawRect(new Rect2(w * 0.50f, h * 0.30f, w * 0.40f, h * 0.10f), new Color(0.86f, 0.90f, 0.95f));
                    DrawCircle(new Vector2(w * 0.22f, h * 0.62f), w * 0.05f, new Color(0.36f, 0.84f, 0.42f));
                    break;
                }
                default:
                    DrawDocLike(w, h, Kind);
                    break;
            }
        }

        /// <summary>文档类图标：白纸 + 折角 + 内容暗示（表格/图/照片/文本）</summary>
        private void DrawDocLike(float w, float h, string kind)
        {
            var page = new StyleBoxFlat { BgColor = Colors.White, BorderColor = new Color(0.68f, 0.72f, 0.78f), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
            page.SetCornerRadiusAll(4);
            DrawStyleBox(page, new Rect2(w * 0.20f, h * 0.06f, w * 0.60f, h * 0.88f));
            var fold = new Color(0.80f, 0.84f, 0.90f);
            DrawPolygon(new[]
            {
                new Vector2(w * 0.62f, h * 0.06f),
                new Vector2(w * 0.80f, h * 0.06f),
                new Vector2(w * 0.80f, h * 0.26f),
            }, new[] { fold, fold, fold });

            switch (kind)
            {
                case "chart":
                    DrawPolyline(new[]
                    {
                        new Vector2(w * 0.30f, h * 0.68f),
                        new Vector2(w * 0.40f, h * 0.44f),
                        new Vector2(w * 0.50f, h * 0.62f),
                        new Vector2(w * 0.60f, h * 0.30f),
                        new Vector2(w * 0.70f, h * 0.56f),
                    }, new Color(0.25f, 0.45f, 0.85f), 2.2f, true);
                    break;
                case "table":
                {
                    var g = new Color(0.30f, 0.60f, 0.35f);
                    for (int i = 0; i < 3; i++)
                        DrawRect(new Rect2(w * 0.30f, h * 0.38f + i * h * 0.14f, w * 0.40f, 1.8f), g);
                    DrawRect(new Rect2(w * 0.30f, h * 0.36f, 1.8f, h * 0.36f), g);
                    DrawRect(new Rect2(w * 0.49f, h * 0.36f, 1.8f, h * 0.36f), g);
                    DrawRect(new Rect2(w * 0.68f, h * 0.36f, 1.8f, h * 0.36f), g);
                    break;
                }
                case "photo":
                {
                    DrawRect(new Rect2(w * 0.28f, h * 0.34f, w * 0.44f, h * 0.30f), new Color(0.20f, 0.24f, 0.32f));
                    DrawPolygon(new[]
                    {
                        new Vector2(w * 0.30f, h * 0.62f),
                        new Vector2(w * 0.42f, h * 0.44f),
                        new Vector2(w * 0.52f, h * 0.62f),
                    }, new[] { new Color(0.55f, 0.68f, 0.55f), new Color(0.55f, 0.68f, 0.55f), new Color(0.55f, 0.68f, 0.55f) });
                    DrawCircle(new Vector2(w * 0.62f, h * 0.42f), w * 0.035f, new Color(0.95f, 0.85f, 0.45f));
                    break;
                }
                case "doc":
                case "text":
                default:
                    for (int i = 0; i < 4; i++)
                        DrawRect(new Rect2(w * 0.30f, h * 0.38f + i * h * 0.12f, w * (i == 3 ? 0.20f : 0.40f), 2f), new Color(0.55f, 0.58f, 0.64f));
                    break;
            }
        }
    }

    /// <summary>未读小圆点</summary>
    private partial class UnreadDot : Control
    {
        public override void _Draw()
        {
            DrawCircle(Size / 2f, Mathf.Min(Size.X, Size.Y) / 2f, new Color(0.20f, 0.44f, 0.90f));
        }
    }

    /// <summary>磁盘占用条</summary>
    private partial class DriveBar : Control
    {
        public int Percent;
        public bool Warn;

        public override void _Draw()
        {
            var bg = new Rect2(0, 0, Size.X, Size.Y);
            var bgStyle = new StyleBoxFlat { BgColor = new Color(0.85f, 0.87f, 0.90f), BorderColor = new Color(0.70f, 0.73f, 0.78f), BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 };
            bgStyle.SetCornerRadiusAll(4);
            DrawStyleBox(bgStyle, bg);

            float p = Mathf.Clamp(Percent, 0, 100) / 100f;
            if (p <= 0.001f)
                return;
            var fill = new StyleBoxFlat
            {
                BgColor = Warn || Percent >= 90 ? new Color(0.86f, 0.30f, 0.26f)
                    : Percent >= 70 ? new Color(0.90f, 0.68f, 0.22f)
                    : new Color(0.32f, 0.62f, 0.86f),
            };
            fill.SetCornerRadiusAll(4);
            DrawStyleBox(fill, new Rect2(1, 1, (Size.X - 2) * p, Size.Y - 2));
        }
    }

    /// <summary>XRD 曲线：画个像模像样的衍射图（三个峰 + 背景起伏）</summary>
    private partial class XrdChartView : Control
    {
        public override void _Draw()
        {
            var axis = new Color(0.62f, 0.66f, 0.72f);
            float padL = 24, padB = 26, padT = 14, padR = 10;
            float w = Size.X - padL - padR;
            float h = Size.Y - padT - padB;

            DrawLine(new Vector2(padL, padT), new Vector2(padL, padT + h), axis, 1.6f);
            DrawLine(new Vector2(padL, padT + h), new Vector2(padL + w, padT + h), axis, 1.6f);

            // 三个高斯峰叠一点背景噪声（固定相位，每帧一样）
            var pts = new List<Vector2>();
            var peaks = new (float pos, float amp, float width)[]
            {
                (0.18f, 0.72f, 0.020f),
                (0.42f, 1.00f, 0.014f),
                (0.68f, 0.52f, 0.018f),
            };
            int n = 260;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float y = 0.06f + 0.03f * Mathf.Sin(t * 40f) + 0.02f * Mathf.Sin(t * 97f + 1.7f);
                foreach (var (pos, amp, width) in peaks)
                {
                    float d = (t - pos) / width;
                    y += amp * Mathf.Exp(-0.5f * d * d);
                }
                y = Mathf.Min(1f, y);
                pts.Add(new Vector2(padL + t * w, padT + h - y * h * 0.96f));
            }
            DrawPolyline(pts.ToArray(), new Color(0.35f, 0.90f, 0.55f), 2.0f, true);

            var font = GetThemeDefaultFont();
            DrawString(font, new Vector2(padL, Size.Y - 6), "10      30      50      70      90", HorizontalAlignment.Left, w, 15, new Color(0.55f, 0.60f, 0.66f));
        }
    }

    /// <summary>电镜照的"抽象版"：深底 + 几颗团聚的球颗粒</summary>
    private partial class SemBlobView : Control
    {
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.10f, 0.11f, 0.13f));
            var rng = new RandomNumberGenerator();
            rng.Seed = 318;
            for (int i = 0; i < 26; i++)
            {
                var c = new Vector2(rng.RandfRange(0.06f, 0.94f) * Size.X, rng.RandfRange(0.08f, 0.92f) * Size.Y);
                float r = rng.RandfRange(10, 34);
                var baseCol = new Color(0.72f, 0.75f, 0.80f, rng.RandfRange(0.55f, 0.9f));
                DrawCircle(c, r, baseCol);
                DrawCircle(c - new Vector2(r * 0.25f, r * 0.25f), r * 0.5f, new Color(0.88f, 0.90f, 0.94f, 0.5f));
            }
            // 比例尺
            DrawLine(new Vector2(Size.X - 150, Size.Y - 28), new Vector2(Size.X - 30, Size.Y - 28), Colors.White, 2.4f);
            var font = GetThemeDefaultFont();
            DrawString(font, new Vector2(Size.X - 150, Size.Y - 34), "2 μm", HorizontalAlignment.Left, 120, 15, new Color(0.92f, 0.94f, 0.96f));
        }
    }
}
