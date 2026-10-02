using Godot;
using System;

/// <summary>
/// 场景过渡控制器（淡入淡出效果）
///
/// 【Day 2 知识点 - Tween 动画】
/// Tween（补间动画）就是"从 A 平滑变化到 B"：
///   - 透明度从 0 → 1 = 淡入（画面变黑）
///   - 透明度从 1 → 0 = 淡出（画面变清）
/// 不用一帧帧画，Godot 自动帮你算中间的每一帧
/// </summary>
public partial class TransitionManager : CanvasLayer
{
    private static TransitionManager instance;
    private ColorRect fadeRect;

    public override void _Ready()
    {
        instance = this;
        Layer = 100; // 确保在最上层

        // 创建一个覆盖全屏的黑色矩形
        fadeRect = new ColorRect();
        fadeRect.Color = new Color(0, 0, 0, 0); // 初始完全透明
        AddChild(fadeRect);

        // 设置全屏
        fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore; // 不阻挡点击
    }

    /// <summary>
    /// 淡入效果（画面逐渐变黑），然后切换场景，再淡出
    /// </summary>
    public static async void FadeToScene(string scenePath)
    {
        if (instance == null)
        {
            return;
        }

        var rect = instance.fadeRect;
        var tree = instance.GetTree();

        // 第一阶段：淡入（画面变黑）
        rect.MouseFilter = Control.MouseFilterEnum.Stop; // 过渡期间阻挡点击
        var tweenIn = instance.CreateTween();
        tweenIn.TweenProperty(rect, "color", new Color(0, 0, 0, 1), 0.4f);
        await instance.ToSignal(tweenIn, Tween.SignalName.Finished);

        // 第二阶段：切换场景（直接调用，不经过 GameManager）
        tree.ChangeSceneToFile(scenePath);

        // 等一帧让新场景加载完
        await instance.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        // 第三阶段：淡出（画面变清）
        var tweenOut = instance.CreateTween();
        tweenOut.TweenProperty(rect, "color", new Color(0, 0, 0, 0), 0.4f);
        await instance.ToSignal(tweenOut, Tween.SignalName.Finished);

        rect.MouseFilter = Control.MouseFilterEnum.Ignore; // 恢复点击
    }

    /// <summary>
    /// 简单的淡入效果（开场用）
    /// </summary>
    public static async void FadeIn()
    {
        if (instance == null) return;

        var rect = instance.fadeRect;
        rect.Color = new Color(0, 0, 0, 1); // 先全黑

        var tween = instance.CreateTween();
        tween.TweenProperty(rect, "color", new Color(0, 0, 0, 0), 0.8f);
        await instance.ToSignal(tween, Tween.SignalName.Finished);
    }
}
