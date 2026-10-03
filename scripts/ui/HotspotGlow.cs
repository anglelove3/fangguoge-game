using Godot;

/// <summary>
/// 热点微光提示
///
/// 【为什么做这个】
/// 第一版 DEMO 里，可点击的物体会一直显示一个方框 + "看看电脑"字样，
/// 玩家还没探索就全知道了，少了发现的乐趣。
/// 新方案：平时完全隐形（只有鼠标移上去才发光），
/// 玩家超过一段时间没点任何东西，才轻轻"呼吸"一下提示这里有东西。
///
/// 【原理】
/// 每个热点按钮身上挂一个透明 Panel（铺满按钮区域、不接收鼠标），
/// 把它用 Tween 从透明渐变到微亮再淡回去，就是一次"呼吸"。
/// 按钮本身负责接收点击，发光层只负责好看。
/// </summary>
public static class HotspotGlow
{
    /// <summary>
    /// 给热点按钮挂一个隐形发光层，返回这个 Panel 以便后面控制它
    /// </summary>
    public static Panel Attach(Button button)
    {
        var panel = new Panel();
        panel.Name = "Glow";
        panel.MouseFilter = Control.MouseFilterEnum.Ignore; // 不挡点击
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // 暖黄色微光样式（和主菜单的奶油色系一致）
        var style = new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.93f, 0.72f, 0.10f),
            BorderColor = new Color(1f, 0.9f, 0.65f, 0.45f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(14);
        style.CornerDetail = 14;
        panel.AddThemeStyleboxOverride("panel", style);

        // 初始完全透明
        panel.Modulate = new Color(1, 1, 1, 0);

        button.AddChild(panel);
        return panel;
    }

    /// <summary>
    /// 呼吸一次：淡入 → 停留 → 淡出
    /// delay 用来让多个热点错开闪，像波浪一样
    /// </summary>
    public static void Pulse(Panel glow, float delay = 0f)
    {
        if (glow == null || !GodotObject.IsInstanceValid(glow))
            return;

        var tween = glow.CreateTween();
        if (delay > 0f)
            tween.TweenInterval(delay);
        tween.TweenProperty(glow, "modulate:a", 1.0f, 0.45f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        tween.TweenInterval(0.55f);
        tween.TweenProperty(glow, "modulate:a", 0.0f, 0.7f).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }
}
