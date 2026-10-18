using Godot;

/// <summary>
/// 调试面板 —— 按 F12 才出现的"开发者仪表盘"
///
/// 【第十一轮改动：为什么默认要藏起来】
/// 第十轮试玩反馈：面板从主菜单开始就一直挂在右上角，
/// 好感度/勇气值/隐藏物品数量、甚至内部结局名 HermitCrab 全都写在画面上。
/// 设计稿里好感度和勇气值是"隐藏属性"，玩家不该看见——所以：
///   默认隐藏（Visible = false），按 F12 才显示，再按一次关掉。
///
/// 【知识点 - CanvasLayer.Visible】
/// 整个 CanvasLayer 可以直接关掉，关掉后它下面的控件一个都不画，
/// 但节点还在树里，所以按 F12 随时能再亮出来。
/// </summary>
public partial class DebugPanel : CanvasLayer
{
    private Label debugLabel;
    private bool panelVisible; // 默认 false：不显示

    private Label statsLabel;      // F3：帧耗时/节点数叠层（第四轮反馈 5）
    private bool statsVisible;
    private float statsAcc;

    public override void _Ready()
    {
        Layer = 99; // 在过渡层之下，普通场景之上

        // 创建面板容器
        var panel = new PanelContainer();
        AddChild(panel);

        // 面板样式：半透明黑色背景
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0, 0, 0, 0.7f);
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.ContentMarginLeft = 12;
        style.ContentMarginTop = 8;
        style.ContentMarginRight = 12;
        style.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", style);

        // 放在右上角
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -280;
        panel.OffsetTop = 10;
        panel.OffsetRight = -10;

        // 创建文字标签
        debugLabel = new Label();
        debugLabel.AddThemeFontSizeOverride("font_size", 14);
        debugLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
        panel.AddChild(debugLabel);

        // ---- F3 性能叠层（第四轮反馈 5）：常驻左上角，随时看帧耗时 ----
        statsLabel = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        statsLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        statsLabel.Position = new Vector2(14, 84);
        statsLabel.AddThemeFontSizeOverride("font_size", 15);
        statsLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.98f, 1.0f, 0.92f));
        statsLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        statsLabel.AddThemeConstantOverride("outline_size", 6);
        AddChild(statsLabel);

        Visible = false; // ← 关键：默认不显示
        GD.Print("[调试面板] 已加载（默认隐藏，按 F12 显示 / 隐藏；F3 帧耗时叠层）");
    }

    /// <summary>F3 叠层：帧耗时/节点/绘制，每 0.25 秒刷一次</summary>
    private void TickStats(double delta)
    {
        if (!statsVisible || statsLabel == null)
            return;
        statsAcc += (float)delta;
        if (statsAcc < 0.25f)
            return;
        statsAcc = 0f;
        float ms = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
        float fps = (float)Performance.GetMonitor(Performance.Monitor.TimeFps);
        int nodes = (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        int draws = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        statsLabel.Text = $"FPS {fps:F0}   帧 {ms:F2} ms   节点 {nodes}   绘制 {draws}";
    }

    /// <summary>F12 切换显示/隐藏</summary>
    ///
    /// 【知识点 - 为什么写在 _UnhandledKeyInput 而不是 _Input】
    /// Godot 只会把"绑定了动作（action）"的按键送进 _Input，比如空格=ui_select、
    /// 回车=ui_accept、Tab=ui_focus_next。F12 没有绑定任何动作，写在 _Input 里
    /// 根本收不到（第十一轮自动化测试抓出来的：真玩家按 F12 也不会有反应）。
    /// 没绑动作的按键要走 _UnhandledKeyInput —— 它是"没人处理的按键"的兜底入口。
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.F12)
            {
                panelVisible = !panelVisible;
                Visible = panelVisible;
                GD.Print($"[调试面板] {(panelVisible ? "显示" : "隐藏")}");
            }
            else if (key.Keycode == Key.F3)
            {
                statsVisible = !statsVisible;
                statsLabel.Visible = statsVisible;
                GD.Print($"[调试面板] 帧耗时叠层：{(statsVisible ? "开" : "关")}");
            }
            else if (key.Keycode == Key.F11)
            {
                // 数值飘字开关（第十八轮拍板点③）：玩家侧默认不飘，
                // 开发自查数值时按 F11 临时打开。
                StatToast.DeveloperToasts = !StatToast.DeveloperToasts;
                GD.Print($"[调试面板] 数值飘字（StatToast）：{(StatToast.DeveloperToasts ? "开" : "关")}");
            }
        }
    }

    /// <summary>
    /// _Process：只在显示时刷新内容（隐藏时零开销）
    /// </summary>
    public override void _Process(double delta)
    {
        // F3 叠层独立于 F12 面板：只开叠层时也要刷新
        TickStats(delta);
        if (!panelVisible || GameManager.Instance == null)
            return;

        var gm = GameManager.Instance;
        var ending = gm.GetEndingType();
        int total = DataStore.HiddenItemTotal;

        // 关系账速览（第十七轮）：好友数 + 玩家改过的备注条数，
        // 自动化回归直接读这两个数断言"加好友/改备注有没有真的落进存档"。
        int friended = 0, remarked = 0;
        foreach (var pair in gm.Relationships)
        {
            if (pair.Value.friended) friended++;
            if (!string.IsNullOrEmpty(pair.Value.remark)) remarked++;
        }

        debugLabel.Text =
            $"章节: {gm.CurrentChapter}\n" +
            $"好感度: {gm.Affection}/100\n" +
            $"勇气值: {gm.Courage}/100\n" +
            $"隐藏物品: {gm.HiddenItemsFound}/{total}\n" +
            $"结局倾向: {DataStore.Text($"ending.{(int)ending}")}\n" +
            $"选择记录: {gm.ChoiceCount} 条\n" +
            $"好友: {friended} 人 / 备注: {remarked} 条\n" +
            $"飘字: {(StatToast.DeveloperToasts ? "开(F11)" : "关(F11)")}";

    }
}
