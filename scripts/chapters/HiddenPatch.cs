using Godot;
using System;

/// <summary>
/// 隐藏物贴片（第二十轮 F1）：物件从背景图里拆成独立一层——
/// 收走之后从场景里消失，不再"背包里有了、桌上还摆着"。
///
/// 【两种用法】
///   1. 原物画死在底图里（第二章窗台的打火机）：
///      patch = 含原物的底图矩形（和底图像素一致，贴上去无缝），
///      clean = 抹净版矩形。收走时 patch 淡出，露出净底——物件"没了"。
///   2. 原物没画在底图里（第四章耳机 / 第八章礼花筒）：
///      patch = 物件贴图（透明底），cleanPath 传 null，收走时整片淡出。
///
/// 【挂在哪】挂在对应的背景 TextureRect 下面当子节点——
/// 背景 crossfade/淡入淡出时贴片跟着走，永远不穿帮。
/// </summary>
public partial class HiddenPatch : Control
{
    private TextureRect top;   // 原物层（收集时淡出这层）
    private Rect2 artRect;
    private Action onGone;

    /// <summary>美术图坐标版（1792×1024 像素矩形）</summary>
    public static HiddenPatch Create(string patchPath, string cleanPath, Rect2 artRect, bool exactFill = false)
    {
        var host = new HiddenPatch { artRect = artRect, MouseFilter = MouseFilterEnum.Ignore };

        // 净底层（垫在下面；null = 没有净底，整片一起淡）
        if (!string.IsNullOrEmpty(cleanPath) && ResourceLoader.Exists(cleanPath))
            host.AddChild(MakeLayer(cleanPath, exactFill: true));

        // 原物层（收集时淡出这层）
        if (ResourceLoader.Exists(patchPath))
        {
            host.top = MakeLayer(patchPath, exactFill);
            host.AddChild(host.top);
        }
        return host;
    }

    /// <summary>设计稿比例版（老锚点数字直接用）</summary>
    public static HiddenPatch CreateAtFraction(string patchPath, string cleanPath, Rect2 frac, bool exactFill = false)
        => Create(patchPath, cleanPath, ArtAnchor.FractionToArt(frac), exactFill);

    private static TextureRect MakeLayer(string path, bool exactFill)
    {
        return new TextureRect
        {
            Texture = GD.Load<Texture2D>(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            // exactFill = 贴片是"底图矩形"，必须精确铺满（和底图像素对齐）；
            // 物件贴图（透明底）用 KeepAspectCentered，等比缩放不变形。
            StretchMode = exactFill
                ? TextureRect.StretchModeEnum.Scale
                : TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    public override void _Ready()
    {
        foreach (var child in GetChildren())
            if (child is TextureRect tr)
                tr.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 进树之后才能 Track（要拿视口尺寸），窗口一变跟着重算
        ArtAnchor.Track(this, artRect);
    }

    /// <summary>玩家拿走了：上层淡出（露出净底），物件从场景里消失。淡完回调。</summary>
    public void Disappear(Action gone = null)
    {
        if (top == null || !GodotObject.IsInstanceValid(top))
        {
            gone?.Invoke();
            return;
        }
        onGone = gone;
        var tw = top.CreateTween();
        tw.TweenProperty(top, "modulate:a", 0f, 0.55f);
        tw.TweenCallback(Callable.From(() => onGone?.Invoke()));
    }

    /// <summary>读档进来就已经拿过了：不演动画，直接不显示</summary>
    public void HideImmediately() => Visible = false;
}
