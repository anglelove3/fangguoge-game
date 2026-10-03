using Godot;

/// <summary>
/// 热点反馈（无框 + 星光版）
///
/// 【为什么做这个】
/// 第一版是"常显方框"，玩家还没探索就全知道了；
/// 第二版隐形了，但悬停/点击/发呆时还是会冒出一个琥珀色小方框，试玩反馈"方框太出戏"。
/// 这一版彻底去掉所有"框"，只用"光"来提示：
///   - 鼠标悬停   → 热点上泛起一层柔光（没有边界感，随鼠标淡入淡出）
///   - 点击瞬间   → 指尖荡开一圈光环 + 冒出两三颗小星光 + 音效
///   - 发呆 12 秒 → 还没找到的物件轻轻"眨"一下柔光
/// 存在感交给鼠标指针（手型）+ 音效 + 光，画面始终保持干净。
///
/// 【原理】
/// 每个热点按钮身上挂一个透明 Control（铺满按钮区域、不接收鼠标），
/// 里面放一张"中间亮、边缘透明"的径向渐变贴图 = 柔光。
/// 星光则是点击瞬间临时生成 2~3 张小图，用 Tween 放大 + 淡出后自动销毁。
/// </summary>
public static class HotspotGlow
{
    // 柔光/星光/光环的纹理只生成一次，所有热点共用（静态缓存）
    private static Texture2D softGlowTexture;
    private static Texture2D starTexture;
    private static Texture2D ringTexture;

    /// <summary>
    /// 给热点按钮挂上"隐形柔光层"，返回这个图层（后面控制它）
    /// </summary>
    public static Control Attach(Button button)
    {
        var root = new Control();
        root.Name = "GlowFeedback";
        root.MouseFilter = Control.MouseFilterEnum.Ignore; // 不挡点击
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var glow = new TextureRect();
        glow.Name = "SoftGlow";
        glow.Texture = GetSoftGlowTexture();
        glow.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        glow.StretchMode = TextureRect.StretchModeEnum.Scale;
        glow.MouseFilter = Control.MouseFilterEnum.Ignore;
        glow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        glow.Modulate = new Color(1, 1, 1, 0); // 初始完全透明
        root.AddChild(glow);

        // 悬停：柔光淡入；移开：淡出（按钮禁用后不再触发，等于"看过了"）
        button.MouseEntered += () => FadeTo(glow, 0.68f, 0.18f);
        button.MouseExited += () => FadeTo(glow, 0f, 0.3f);
        // 点击：立刻收起悬停柔光（禁用后不会再有 MouseExited，这里兜底）
        button.Pressed += () => FadeTo(glow, 0f, 0.25f);

        // 记下"真正点在这个按钮上"的位置（事件自带局部坐标，真实鼠标/自动化/触屏都准）
        button.GuiInput += (InputEvent e) =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                button.SetMeta("last_press_local", mb.Position);
        };

        button.AddChild(root);
        return root;
    }

    /// <summary>
    /// 发呆提示：让柔光轻轻"呼吸"一下（delay 用来让多个热点错开闪）
    /// </summary>
    public static void Pulse(Control glowRoot, float delay = 0f)
    {
        if (glowRoot == null || !GodotObject.IsInstanceValid(glowRoot))
            return;
        var glow = glowRoot.GetNodeOrNull<TextureRect>("SoftGlow");
        if (glow == null)
            return;

        KillTween(glow);
        var tween = glow.CreateTween();
        if (delay > 0f)
            tween.TweenInterval(delay);
        tween.TweenProperty(glow, "modulate:a", 0.6f, 0.5f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        tween.TweenInterval(0.3);
        tween.TweenProperty(glow, "modulate:a", 0f, 0.8f).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        glow.SetMeta("tw", tween);
    }

    /// <summary>
    /// 点击瞬间：指尖冒一圈扩散光环 + 2~3 颗小星光（"找到了！"的小反馈）
    /// 星光用加法混合（暗背景上会发光），光环用普通混合（亮背景上也看得清）
    /// 位置取按钮自己记录的真实点击点（没有记录时退回按钮中心）
    /// </summary>
    public static void Sparkle(Button button)
    {
        if (button == null || !GodotObject.IsInstanceValid(button))
            return;

        Vector2 localPos = button.HasMeta("last_press_local")
            ? button.GetMeta("last_press_local").AsVector2()
            : button.Size / 2f; // 键盘触发等没有鼠标位置的情况：从按钮中心扩散

        SpawnRing(button, localPos);

        var rng = new RandomNumberGenerator();
        rng.Randomize();

        int count = 2 + rng.RandiRange(0, 1); // 2 或 3 颗
        for (int i = 0; i < count; i++)
        {
            var star = new TextureRect();
            star.Texture = GetStarTexture();
            star.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            star.StretchMode = TextureRect.StretchModeEnum.Scale;
            star.MouseFilter = Control.MouseFilterEnum.Ignore;
            star.Size = new Vector2(28, 28);
            star.PivotOffset = new Vector2(14, 14);
            star.Position = localPos - new Vector2(14, 14)
                + new Vector2(rng.RandfRange(-26f, 26f), rng.RandfRange(-26f, 26f));
            star.Rotation = rng.RandfRange(-0.5f, 0.5f);
            star.Scale = Vector2.One * 0.2f;
            star.Modulate = new Color(1f, 0.92f, 0.72f, 0f);
            // 加法混合：亮部叠加，更像"闪光"
            star.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

            button.AddChild(star);

            var tween = star.CreateTween();
            float dur = 0.4f + i * 0.12f;
            Vector2 drift = star.Position + new Vector2(rng.RandfRange(-14f, 14f), rng.RandfRange(-22f, -6f));

            tween.SetParallel(true);
            tween.TweenProperty(star, "scale", Vector2.One * (1.0f + i * 0.3f), dur)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(star, "position", drift, dur)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(star, "modulate:a", 1.0f, dur * 0.35f);
            tween.TweenProperty(star, "modulate:a", 0f, dur * 0.65f).SetDelay(dur * 0.35f);
            tween.Chain().TweenCallback(Callable.From(star.QueueFree));
        }
    }

    /// <summary>
    /// 一圈快速扩散的光环（普通混合的暖金色，白色/亮色画面上也看得见）
    /// </summary>
    private static void SpawnRing(Button button, Vector2 localPos)
    {
        var ring = new TextureRect();
        ring.Texture = GetRingTexture();
        ring.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        ring.StretchMode = TextureRect.StretchModeEnum.Scale;
        ring.MouseFilter = Control.MouseFilterEnum.Ignore;
        ring.Size = new Vector2(120, 120);
        ring.PivotOffset = new Vector2(60, 60);
        ring.Position = localPos - new Vector2(60, 60);
        ring.Scale = Vector2.One * 0.45f;
        ring.Modulate = new Color(1f, 0.75f, 0.38f, 0.9f);

        button.AddChild(ring);

        var tween = ring.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(ring, "scale", Vector2.One * 1.35f, 0.45f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(ring, "modulate:a", 0f, 0.45f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// <summary>柔光淡到指定透明度（先杀掉旧动画，避免悬停/呼吸打架）</summary>
    private static void FadeTo(TextureRect glow, float alpha, float duration)
    {
        if (glow == null || !GodotObject.IsInstanceValid(glow))
            return;
        KillTween(glow);
        var tween = glow.CreateTween();
        tween.TweenProperty(glow, "modulate:a", alpha, duration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        glow.SetMeta("tw", tween);
    }

    private static void KillTween(TextureRect node)
    {
        if (node.HasMeta("tw") && node.GetMeta("tw").As<Tween>() is Tween tween && tween.IsValid())
            tween.Kill();
    }

    // ---------------- 纹理生成（只做一次） ----------------

    /// <summary>柔光纹理：中间亮、边缘透明的径向渐变（暖金色）</summary>
    private static Texture2D GetSoftGlowTexture()
    {
        if (softGlowTexture != null)
            return softGlowTexture;

        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 0.84f, 0.52f, 0.6f));
        gradient.SetColor(1, new Color(1f, 0.84f, 0.52f, 0f));

        softGlowTexture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 256,
            Height = 256,
        };
        return softGlowTexture;
    }

    /// <summary>星光纹理：四角星（两条细长的菱形光带交叉 + 中心亮点），用像素循环现画</summary>
    private static Texture2D GetStarTexture()
    {
        if (starTexture != null)
            return starTexture;

        const int S = 64;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S / 2f;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - half) / half; // 0 = 中心，1 = 边缘
                float dy = Mathf.Abs(y + 0.5f - half) / half;

                // 两条细长光带（横 + 竖），越靠边越细、越淡
                float hBand = Mathf.Max(0f, 1f - dx) * Mathf.Max(0f, 1f - dy * 7f);
                float vBand = Mathf.Max(0f, 1f - dy) * Mathf.Max(0f, 1f - dx * 7f);
                // 中心柔亮的核心
                float core = Mathf.Max(0f, 1f - (dx * dx + dy * dy) * 2.4f);

                float a = Mathf.Clamp(hBand * 0.85f + vBand * 0.85f + core, 0f, 1f);
                a = a * a; // 平方一下，边缘更利落
                img.SetPixel(x, y, new Color(1f, 0.98f, 0.9f, a));
            }
        }

        starTexture = ImageTexture.CreateFromImage(img);
        return starTexture;
    }

    /// <summary>光环纹理：一圈柔边的细圆环（像素循环现画）</summary>
    private static Texture2D GetRingTexture()
    {
        if (ringTexture != null)
            return ringTexture;

        const int S = 128;
        const float half = S / 2f;
        const float ringRadius = 48f;   // 环的半径
        const float thickness = 7f;     // 环的粗细（带柔边）

        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp(1f - Mathf.Abs(dist - ringRadius) / thickness, 0f, 1f);
                a = a * a; // 平方一下，边缘更柔
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        ringTexture = ImageTexture.CreateFromImage(img);
        return ringTexture;
    }
}
