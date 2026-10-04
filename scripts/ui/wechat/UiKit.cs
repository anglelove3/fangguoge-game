using Godot;

/// <summary>
/// 布局小工具：给"不认 margin 常数"的容器（HBox/VBox）补边距。
///
/// 【坑】Godot 里只有 MarginContainer 认 theme 的 margin_* 常数，
/// HBoxContainer / VBoxContainer 上加了也白加（第四轮截图里列表贴边就是这么来的）。
/// 所以统一用这层 MarginContainer 包一下。
/// </summary>
public static class UiKit
{
    public static MarginContainer WrapMargin(Control child, float left, float top, float right, float bottom)
    {
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        m.AddThemeConstantOverride("margin_left", (int)left);
        m.AddThemeConstantOverride("margin_top", (int)top);
        m.AddThemeConstantOverride("margin_right", (int)right);
        m.AddThemeConstantOverride("margin_bottom", (int)bottom);
        m.AddChild(child);
        return m;
    }
}
