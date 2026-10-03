using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 选项面板 UI
///
/// 【知识点 - 动态创建节点】
/// 选项数量不固定（有的选择 2 个选项，有的 3 个），
/// 所以不能用固定按钮，而是在代码里 new 出按钮再 AddChild 到容器里。
/// 这就是"动态生成 UI"，做选择系统、背包系统都靠它。
/// </summary>
public partial class ChoicePanel : Control
{
    private VBoxContainer optionsBox;
    private readonly List<Button> buttons = new();
    private Action<int> callback;

    public override void _Ready()
    {
        optionsBox = GetNode<VBoxContainer>("CenterContainer/OptionsBox");
        Hide(); // 默认隐藏
    }

    /// <summary>
    /// 显示一组选项
    /// </summary>
    public void ShowChoices(List<ChoiceOptionData> options, Action<int> onSelected)
    {
        callback = onSelected;

        // 清掉上一组按钮
        foreach (var b in buttons)
        {
            b.QueueFree();
        }
        buttons.Clear();

        // 动态生成新按钮
        for (int i = 0; i < options.Count; i++)
        {
            var btn = new Button();
            btn.Text = options[i].Text;
            btn.CustomMinimumSize = new Vector2(780, 76);
            btn.AddThemeFontSizeOverride("font_size", 26);

            // 用主菜单同一套配色，保证风格统一
            btn.AddThemeStyleboxOverride("normal", MakeStyle(new Color(0.831f, 0.647f, 0.455f)));
            btn.AddThemeStyleboxOverride("hover", MakeStyle(new Color(0.910f, 0.769f, 0.604f)));
            btn.AddThemeStyleboxOverride("pressed", MakeStyle(new Color(0.722f, 0.537f, 0.306f)));

            int index = i; // 记下编号（不能直接用 i，会被后面的循环改掉）
            btn.Pressed += () => OnOptionPressed(index);

            optionsBox.AddChild(btn);
            buttons.Add(btn);
        }

        Show();
    }

    private void OnOptionPressed(int index)
    {
        Hide();

        var cb = callback;
        callback = null;
        cb?.Invoke(index);
    }

    /// <summary>
    /// 创建一个圆角矩形样式
    /// </summary>
    private static StyleBoxFlat MakeStyle(Color color)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            CornerDetail = 14,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 14,
            ContentMarginBottom = 14
        };
        return style;
    }
}
