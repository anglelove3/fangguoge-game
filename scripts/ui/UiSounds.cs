using Godot;

/// <summary>
/// 按钮音效小助手
///
/// 【知识点 - 递归遍历节点树】
/// 场景里的按钮藏在不同的层级里（标题卡里的、角落里的……），
/// 一个个找出来接线太累。这个工具会"深度优先"地把一颗子树里
/// 所有 Button 都翻出来，统一给它们挂上：
///   鼠标移上去 → 轻轻"嗒"一声（悬停音效）
///   点下去     → "咔"一声（点击音效）
///
/// 用 SetMeta 打个标记，防止同一个按钮被接两次线（声音会叠）。
/// </summary>
public static class UiSounds
{
    /// <summary>给 root 及其所有子孙里的 Button 挂音效</summary>
    public static void WireAll(Node root)
    {
        if (root is Button btn)
            Wire(btn);

        foreach (var child in root.GetChildren())
            WireAll(child);
    }

    /// <summary>给单个按钮挂音效</summary>
    public static void Wire(Button btn, bool hover = true)
    {
        if (btn.HasMeta("ui_sound_wired"))
            return; // 接过线了
        btn.SetMeta("ui_sound_wired", true);

        btn.Pressed += () => AudioManager.Instance?.PlaySfx(AudioManager.SfxClick, -12f);

        if (hover)
            btn.MouseEntered += () => AudioManager.Instance?.PlaySfx(AudioManager.SfxHover, -18f);
    }
}
