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
    /// 【第三轮试玩反馈 · 根因 A】取出这个场景背景美术的"真实像素尺寸"。
    ///
    /// 以前这里写死 1792×1024，但项目里其实有 11 张背景是 **1536×1024**
    /// （宿舍 dorm_night_v3、夜摊 night_stall_v1、河边 river_dusk_v1、海边 beach_day_v1、
    ///   plaza_night 1~3、balcony/dorm_bed/dorm_building/dorm_late_night…）。
    /// 1536 的图按 cover 铺满视口时缩放／裁切都和 1792 那张不一样，
    /// 于是写死的热点、贴片在这几张图上整体"飘"到旁边的东西上——
    /// 表现就是"点烟弹出手机""互动区和返回菜单重叠""手机和窗台对不齐"。
    ///
    /// 现在统一从场景里的 Background.Texture 读真实尺寸：1792 的图数值完全不变，
    /// 1536 的图自动换算，窗口任意比例都对得上。
    /// </summary>
    private static void ResolveArtSize(Node node, out float aw, out float ah)
    {
        aw = ArtW;
        ah = ArtH;
        for (var n = node; n != null; n = n.GetParent())
        {
            var bg = n.GetNodeOrNull<TextureRect>("Background") ?? n as TextureRect;
            var tex = (bg as TextureRect)?.Texture;
            if (tex == null)
                continue;
            int w = tex.GetWidth(), h = tex.GetHeight();
            if (w <= 0 || h <= 0)
                continue;
            aw = w;
            ah = h;
            return;
        }
    }

    /// <summary>
    /// 把"按 1792 设计稿写的矩形"换算到背景图的真实像素空间。
    /// 1792×1024 的图原样返回（老数值零变化）；1536×1024 的图按 16:9 设计稿
    /// 保持屏幕上的位置不变（0.857 倍 + 纵向补 73px，详见 ArtW/ArtH 注释）。
    /// </summary>
    private static Rect2 ToRealArt(Node node, Rect2 r)
    {
        ResolveArtSize(node, out float aw, out float ah);
        if (Mathf.IsEqualApprox(aw, ArtW) && Mathf.IsEqualApprox(ah, ArtH))
            return r;

        float k = aw / ArtW;
        // 纵向：设计稿 1080 高在两种宽度下被裁掉的量不同，实测折算约 (1792-aw)*0.285
        float dy = (ArtW - aw) * 0.2852f;
        return new Rect2(r.Position.X * k, r.Position.Y * k + dy, r.Size.X * k, r.Size.Y * k);
    }

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
        ResolveArtSize(node, out float aw, out float ah);
        var size = vp.GetVisibleRect().Size;
        // cover 铺满：按背景图的真实尺寸算缩放与裁切偏移（不再假设 1792×1024）
        scale = Mathf.Max(size.X / aw, size.Y / ah);
        offset = (size - new Vector2(aw, ah) * scale) * 0.5f;
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
            var r = ToRealArt(node, artRect);
            node.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            node.Position = off + r.Position * s;
            node.Size = r.Size * s;
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
            var pt = ToRealArt(node, new Rect2(artPoint, Vector2.Zero));
            node.Position = off + pt.Position * s;
            node.Scale = Vector2.One * (s / DesignScale) * propScale;
        }

        var vp = node.GetViewport();
        if (vp != null)
            vp.SizeChanged += Apply;
        Apply();
    }
}
