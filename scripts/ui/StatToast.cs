using Godot;
using System;

/// <summary>
/// 数值飘字：好感度 / 勇气值变化提示
///
/// 【为什么做这个】
/// 第一版里选完选项，数值是悄悄变的，玩家根本感觉不到"我的选择有分量"。
/// 现在选完立刻在画面上方弹一下"好感度 +5 / 勇气值 -2"，
/// 停留一秒多再淡出，玩家就知道这个选择算数了。
///
/// 动画：淡入（0.18s）→ 停留（1.2s）→ 淡出（0.45s）
/// 数字为正 = 暖色/水蓝色；为负 = 淡红色（给玩家一点心理冲击）
/// </summary>
public partial class StatToast : Control
{
    private VBoxContainer toastBox;
    private Label affectionLabel;
    private Label courageLabel;
    private Tween activeTween;

    // 数值配色：正面（琥珀 / 水蓝），负面（淡红）
    private static readonly Color AffectionColor = new(1f, 0.85f, 0.5f);
    private static readonly Color CourageColor = new(0.65f, 0.9f, 1f);
    private static readonly Color NegativeColor = new(0.95f, 0.6f, 0.55f);

    public override void _Ready()
    {
        toastBox = GetNode<VBoxContainer>("ToastBox");
        affectionLabel = GetNode<Label>("ToastBox/AffectionLabel");
        courageLabel = GetNode<Label>("ToastBox/CourageLabel");

        Modulate = new Color(1, 1, 1, 0);
        Visible = false;
    }

    /// <summary>
    /// 弹出变化提示（两个都是 0 就不弹，不打扰玩家）
    /// </summary>
    public void ShowDeltas(int affectionDelta, int courageDelta)
    {
        if (affectionDelta == 0 && courageDelta == 0)
            return;

        // 只显示有变化的行
        affectionLabel.Visible = affectionDelta != 0;
        courageLabel.Visible = courageDelta != 0;

        if (affectionDelta != 0)
        {
            affectionLabel.Text = $"{DataStore.Text("toast.affection")} {FormatDelta(affectionDelta)}";
            affectionLabel.AddThemeColorOverride("font_color",
                affectionDelta > 0 ? AffectionColor : NegativeColor);
        }
        if (courageDelta != 0)
        {
            courageLabel.Text = $"{DataStore.Text("toast.courage")} {FormatDelta(courageDelta)}";
            courageLabel.AddThemeColorOverride("font_color",
                courageDelta > 0 ? CourageColor : NegativeColor);
        }

        // 杀掉上一次没播完的动画，重新开始
        if (activeTween != null && activeTween.IsValid())
            activeTween.Kill();

        // 缩放弹出的轴心点设为中心（每次都用最新尺寸算，窗口拉伸也不怕）
        toastBox.PivotOffset = toastBox.Size / 2f;
        toastBox.Scale = new Vector2(0.94f, 0.94f);

        Visible = true;
        Modulate = new Color(1, 1, 1, 0);

        activeTween = CreateTween();
        activeTween.SetParallel(true);

        // 整体淡入
        activeTween.TweenProperty(this, "modulate:a", 1.0f, 0.18f)
            .SetEase(Tween.EaseType.Out);

        // 轻微放大弹出感
        activeTween.TweenProperty(toastBox, "scale", Vector2.One, 0.3f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Back);

        // 停留一下，再淡出
        activeTween.Chain();
        activeTween.TweenInterval(1.2f);
        activeTween.Chain();
        activeTween.TweenProperty(this, "modulate:a", 0.0f, 0.45f)
            .SetEase(Tween.EaseType.In);
        activeTween.Chain();
        activeTween.TweenCallback(Callable.From(() => Visible = false));
    }

    /// <summary>
    /// 立即隐藏（场景切换 / 返回菜单时调用）
    /// </summary>
    public void HideToast()
    {
        if (activeTween != null && activeTween.IsValid())
            activeTween.Kill();
        Visible = false;
        Modulate = new Color(1, 1, 1, 0);
    }

    private static string FormatDelta(int delta)
    {
        return delta > 0 ? $"+{delta}" : $"{delta}";
    }
}
