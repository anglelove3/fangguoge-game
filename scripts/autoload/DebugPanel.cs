using Godot;
using System;

/// <summary>
/// 调试面板 —— 实时显示游戏属性值
///
/// 在游戏画面右上角显示好感度、勇气值、章节编号等信息。
/// 方便开发阶段验证属性系统是否正常工作。
/// 正式发布时可以隐藏或移除。
///
/// 【Day 2 知识点 - _Process 每帧更新】
/// _Process() 每帧都会调用（约60次/秒），
/// 适合用来实时更新显示内容。
/// </summary>
public partial class DebugPanel : CanvasLayer
{
    private Label debugLabel;
    private bool visible = true;

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
        panel.OffsetLeft = -250;
        panel.OffsetTop = 10;
        panel.OffsetRight = -10;

        // 创建文字标签
        debugLabel = new Label();
        debugLabel.AddThemeFontSizeOverride("font_size", 14);
        debugLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
        panel.AddChild(debugLabel);

        GD.Print("[调试面板] 已加载");
    }

    /// <summary>
    /// _Process：每帧调用，实时更新显示内容
    /// </summary>
    public override void _Process(double delta)
    {
        if (!visible || GameManager.Instance == null)
            return;

        var gm = GameManager.Instance;
        var ending = gm.GetEndingType();

        debugLabel.Text =
            $"章节: {gm.CurrentChapter}\n" +
            $"好感度: {gm.Affection}/100\n" +
            $"勇气值: {gm.Courage}/100\n" +
            $"隐藏物品: {gm.HiddenItemsFound}/{GameManager.TotalHiddenItems}\n" +
            $"当前结局: {ending}";
    }

    /// <summary>
    /// 切换显示/隐藏
    /// </summary>
    public void ToggleVisible()
    {
        visible = !visible;
        Visible = visible;
    }
}
