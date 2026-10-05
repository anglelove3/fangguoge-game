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

        Visible = false; // ← 关键：默认不显示
        GD.Print("[调试面板] 已加载（默认隐藏，按 F12 显示 / 隐藏）");
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
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F12)
        {
            panelVisible = !panelVisible;
            Visible = panelVisible;
            GD.Print($"[调试面板] {(panelVisible ? "显示" : "隐藏")}");
        }
    }

    /// <summary>
    /// _Process：只在显示时刷新内容（隐藏时零开销）
    /// </summary>
    public override void _Process(double delta)
    {
        if (!panelVisible || GameManager.Instance == null)
            return;

        var gm = GameManager.Instance;
        var ending = gm.GetEndingType();
        int total = DataStore.HiddenItemTotal;

        debugLabel.Text =
            $"章节: {gm.CurrentChapter}\n" +
            $"好感度: {gm.Affection}/100\n" +
            $"勇气值: {gm.Courage}/100\n" +
            $"隐藏物品: {gm.HiddenItemsFound}/{total}\n" +
            $"结局倾向: {DataStore.Text($"ending.{(int)ending}")}\n" +
            $"选择记录: {gm.ChoiceCount} 条";
    }
}
