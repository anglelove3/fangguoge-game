using Godot;

/// <summary>
/// 第十五轮：把"热点 / 摆件"钉在美术图上。
///
/// 【为什么要有这个】
/// 所有背景美术都是 1792×1024，在场景里按 cover 铺满视口：
/// 窗口一旦不是 16:9（比如玩家把窗口拉得更宽），美术会被放大并上下裁切，
/// 画面里的东西（桌上的手机、烟灰缸、酒杯）跟着移动——
/// 可热点要是写死视口坐标，就还停在原地，于是"点手机点不到""酒杯飘在桌子上方"。
///
/// 这里统一换算：热点先按"美术图坐标"登记一次，
/// 之后每次窗口尺寸变化都重算成当前视口坐标，永远贴在画面里那个东西上。
/// </summary>
public static class ArtAnchor
{
    public const float ArtW = 1792f;
    public const float ArtH = 1024f;

    /// <summary>16:9 设计稿（1920×1080）下美术的显示比例，用作摆件缩放的基准</summary>
    public const float DesignScale = 1920f / ArtW;

    /// <summary>
    /// 把"1920×1080 设计稿里的比例矩形"换算成美术图坐标
    /// （老的锚点数字直接搬过来就是零跳动——16:9 时两者恰好一一对应，
    ///   换算只在窗口变成别的比例时才起作用）。
    /// </summary>
    public static Rect2 FractionToArt(Rect2 frac)
    {
        const float S = ArtW / 1920f;            // 设计稿像素 → 美术像素 的比例（1792/1920）
        float visH = 1080f * S;                  // 设计稿的 1080 高在美术里 = 1008
        float offY = (ArtH - visH) * 0.5f;       // 16:9 时美术上下各被裁掉 8px
        return new Rect2(
            frac.Position.X * ArtW,
            frac.Position.Y * visH + offY,
            frac.Size.X * ArtW,
            frac.Size.Y * visH);
    }

    /// <summary>按"1920×1080 设计稿的比例矩形"登记热点（老锚点数字可直接用）</summary>
    public static void TrackFraction(Control node, Rect2 frac) => Track(node, FractionToArt(frac));

    private static bool TryGetMapping(Node node, out float scale, out Vector2 offset)
    {
        scale = 1f;
        offset = Vector2.Zero;
        var vp = node.GetViewport();
        if (vp == null)
            return false;
        var size = vp.GetVisibleRect().Size;
        scale = Mathf.Max(size.X / ArtW, size.Y / ArtH);
        offset = (size - new Vector2(ArtW, ArtH) * scale) * 0.5f;
        return true;
    }

    /// <summary>
    /// 把一个热点按钮钉在美术图的 rect 上（坐标是 1792×1024 的像素）。
    /// 窗口尺寸一变就自动重算。
    /// </summary>
    public static void Track(Control node, Rect2 artRect)
    {
        void Apply()
        {
            if (!GodotObject.IsInstanceValid(node) || !TryGetMapping(node, out float s, out var off))
                return;
            node.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            node.Position = off + artRect.Position * s;
            node.Size = artRect.Size * s;
        }

        var vp = node.GetViewport();
        if (vp != null)
            vp.SizeChanged += Apply;
        Apply();
    }

    /// <summary>
    /// 把一个 Node2D 摆件（酒杯、火星子这类）按美术点摆位。
    /// propScale 是"摆件相对美术的额外缩放"（设计上想让道具显得多大）。
    /// </summary>
    public static void Track2D(Node2D node, Vector2 artPoint, float propScale = 1f)
    {
        void Apply()
        {
            if (!GodotObject.IsInstanceValid(node) || !TryGetMapping(node, out float s, out var off))
                return;
            node.Position = off + artPoint * s;
            node.Scale = Vector2.One * (s / DesignScale) * propScale;
        }

        var vp = node.GetViewport();
        if (vp != null)
            vp.SizeChanged += Apply;
        Apply();
    }
}
