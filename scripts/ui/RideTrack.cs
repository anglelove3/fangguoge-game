using Godot;
using System.Collections.Generic;

/// <summary>
/// 公路滚行层（第二十二轮 F18 重做）：从"看演出"升级成"轻轻开车"。
///
/// 【重做前的问题（试玩反馈 #14）】
///   背景左右晃 = 在荡秋千不在骑车；掠过物是浮在半空的 UI 面板；进出场硬切。
///
/// 【重做后】
///   1. 伪 3D 公路：下半屏程序绘制的路面（灭点透视 + 中央虚线滚动 + 路边电线杆
///      从远处涌出），上半屏保留水彩底图当天空远景——速度感从这里来。
///   2. 骑手剪影：人和电动车在画面下方，←→ / A D 轻轻左右挪（无惩罚、无碰撞，
///      纯参与感——这条路没有对错，只有风）。
///   3. F19 骑行途中微信横幅：ShowBanner() 从顶部滑入，红点未读照常；到节点停车可读。
///   4. F19 聊天外彩蛋：AddFlavor() 暖黄小卡掠过（服务区橘猫 / 货车标语 / 电台 FM）。
///   5. 入出场 0.8 秒音画过渡：FadeIn() / FadeOut()。
///
/// 【与章节脚本的关系】Ch13 是导演，RideTrack 是舞台。
/// 旧 API（Start/Stop/SetTint/SetRain/SetBattery/AddSign/AddMarker/PauseForChoice/Resume）
/// 全部保留，章节脚本零破坏。
/// </summary>
public partial class RideTrack : Control
{
    // ==================== 节点 ====================

    private TextureRect bg;            // 公路底图（上半屏远景）
    private Control passingRoot;       // 掠过物容器
    private ColorRect darken;          // 压暗层
    private ColorRect tintLayer;       // 天光染色层
    private GpuParticles2D rain;       // 雨幕
    private AudioStreamPlayer engine;  // 电机声循环
    private RoadLayer road;            // 伪 3D 公路 + 骑手（第二十二轮重做）
    private Panel banner;              // F19 微信横幅
    private Label bannerName;
    private Label bannerText;
    private Tween bannerTween;

    // 电量 HUD
    private Control batteryHud;
    private Panel batteryFrame;
    private ColorRect batteryFill;
    private Label batteryLabel;

    // ==================== 状态 ====================

    private bool running;
    private double spawnTimer;
    private double signCountdown = 6;
    private readonly System.Random rng = new();

    // ==================== 天光配方 ====================

    private static readonly Dictionary<string, (Color tint, Color dark)> Tints =
        new()
        {
            { "morning", (new Color(0.55f, 0.65f, 0.85f, 0.16f), new Color(0.10f, 0.12f, 0.22f, 0.10f)) },
            { "noon",    (new Color(1.00f, 0.95f, 0.82f, 0.08f), new Color(0.10f, 0.10f, 0.15f, 0.04f)) },
            { "rain",    (new Color(0.35f, 0.45f, 0.60f, 0.22f), new Color(0.05f, 0.08f, 0.16f, 0.28f)) },
            { "dusk",    (new Color(0.95f, 0.52f, 0.25f, 0.18f), new Color(0.16f, 0.08f, 0.18f, 0.16f)) },
            { "night",   (new Color(0.30f, 0.38f, 0.60f, 0.26f), new Color(0.03f, 0.04f, 0.12f, 0.38f)) },
        };

    // ==================== 构建 ====================

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        bg = new TextureRect
        {
            Name = "RoadBg",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 第三轮 F5：照片带 → 程序路面的渐变融合（旧版上下两套画风硬切，衔接突兀）
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.22f, 0.22f, 0.24f, 0f));
        grad.SetColor(1, new Color(0.22f, 0.22f, 0.24f, 0.88f));
        var gradTex = new GradientTexture2D
        {
            Gradient = grad,
            Width = 8,
            Height = 160,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
        };
        var blendBand = new TextureRect
        {
            Texture = gradTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        blendBand.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        blendBand.AnchorTop = 0.30f;
        blendBand.AnchorBottom = 0.60f;
        blendBand.OffsetLeft = 0; blendBand.OffsetRight = 0;
        blendBand.OffsetTop = 0; blendBand.OffsetBottom = 0;
        AddChild(blendBand);

        tintLayer = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.Transparent };
        darken = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.Transparent };
        foreach (var layer in new Control[] { tintLayer, darken })
        {
            layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(layer);
        }

        passingRoot = new Control { MouseFilter = MouseFilterEnum.Ignore };
        passingRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(passingRoot);

        rain = BuildRain();
        AddChild(rain);

        BuildBatteryHud();
        BuildBanner();

        engine = new AudioStreamPlayer { VolumeDb = -60f };
        AddChild(engine);
    }

    /// <summary>公道路图层最后挂（盖住下半屏），需要 bg 就绪后再加；由章节场景树保证顺序</summary>
    public override void _EnterTree()
    {
        // RoadLayer 在 _Ready 里加不合适（要盖在 tint 之上、banner 之下）——延迟到首帧前
        CallDeferred(nameof(EnsureRoadLayer));
    }

    private void EnsureRoadLayer()
    {
        if (road != null)
            return;
        road = new RoadLayer { G = this, MouseFilter = MouseFilterEnum.Ignore };
        road.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(road);
    }

    public void SetBackground(Texture2D tex) => bg.Texture = tex;

    public void SetEngineStream(AudioStream stream)
    {
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        engine.Stream = stream;
    }

    // ==================== 入出场过渡（F18 保底项） ====================

    /// <summary>0.8 秒音画淡入（配合 Visible = true 使用）</summary>
    public void FadeIn(float seconds = 0.8f)
    {
        Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 1f, seconds).SetEase(Tween.EaseType.Out);
    }

    /// <summary>0.8 秒音画淡出（invisible 由章节脚本随后设置）</summary>
    public void FadeOut(float seconds = 0.8f)
    {
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, seconds).SetEase(Tween.EaseType.In);
    }

    // ==================== 行进控制 ====================

    public void Start(float engineDb = -14f)
    {
        running = true;
        road?.SetMoving(true);
        if (engine.Stream != null)
        {
            if (!engine.Playing)
                engine.Play();
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", engineDb, 2.0f);
        }
    }

    public void PauseForChoice()
    {
        road?.SetMoving(false);
        if (engine.Playing)
        {
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", -34f, 0.8f);
        }
    }

    public void Resume(float engineDb = -14f)
    {
        running = true;
        road?.SetMoving(true);
        if (engine.Stream != null)
        {
            if (!engine.Playing)
                engine.Play();
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", engineDb, 1.2f);
        }
    }

    public void Stop(float fade = 1.4f)
    {
        running = false;
        road?.SetMoving(false);
        if (engine.Playing)
        {
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", -60f, fade);
            tw.TweenCallback(Callable.From(() => engine.Stop())).SetDelay(fade + 0.1f);
        }
    }

    // ==================== 天光 / 雨 / 电量 ====================

    public void SetTint(string key)
    {
        if (!Tints.TryGetValue(key, out var recipe))
            return;
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(tintLayer, "color", recipe.tint, 1.6f);
        tw.TweenProperty(darken, "color", recipe.dark, 1.6f);
    }

    public void SetRain(bool on, float fade = 1.2f)
    {
        rain.Emitting = on;
        var tw = CreateTween();
        tw.TweenProperty(rain, "amount_ratio", on ? 1.0f : 0.0f, fade);
    }

    public void SetBattery(float percent, string text = "")
    {
        batteryHud.Visible = true;
        percent = Mathf.Clamp(percent, 0f, 100f);
        batteryFill.AnchorRight = percent / 100f;

        var c = percent > 30f
            ? new Color(0.72f, 0.78f, 0.62f)
            : percent > 12f
                ? new Color(0.90f, 0.68f, 0.38f)
                : new Color(0.85f, 0.36f, 0.30f);
        batteryFill.Color = c;
        batteryLabel.Text = string.IsNullOrEmpty(text) ? $"电量 {percent:0}%" : text;
    }

    public void HideBattery() => batteryHud.Visible = false;

    // ==================== F19 微信横幅 ====================

    /// <summary>骑行途中微信消息横幅：顶部滑入 2.6 秒自动收起（红点照常挂在手机上，停车可读）</summary>
    public void ShowBanner(string contact, string text)
    {
        bannerName.Text = contact;
        bannerText.Text = text;
        banner.Visible = true;

        bannerTween?.Kill();
        banner.Modulate = new Color(1, 1, 1, 0);
        banner.Position = new Vector2(160, -80);
        bannerTween = CreateTween();
        bannerTween.TweenProperty(banner, "modulate:a", 1f, 0.25f);
        bannerTween.Parallel().TweenProperty(banner, "position:y", 28f, 0.3f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        bannerTween.TweenInterval(2.6f);
        bannerTween.TweenProperty(banner, "modulate:a", 0f, 0.35f);
        bannerTween.Parallel().TweenProperty(banner, "position:y", -80f, 0.3f);
        bannerTween.TweenCallback(Callable.From(() => banner.Visible = false));
    }

    private void BuildBanner()
    {
        banner = new Panel { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.10f, 0.88f),
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            BorderColor = new Color(0.16f, 0.62f, 0.30f, 0.7f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        };
        banner.AddThemeStyleboxOverride("panel", style);

        banner.AnchorLeft = 0f; banner.AnchorRight = 0f;
        banner.AnchorTop = 0f; banner.AnchorBottom = 0f;
        banner.OffsetLeft = 160; banner.OffsetRight = 1120;
        banner.OffsetTop = -80; banner.OffsetBottom = 20;
        banner.ZIndex = 20;
        AddChild(banner);

        var icon = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        var iconStyle = new StyleBoxFlat { BgColor = new Color(0.16f, 0.62f, 0.30f) };
        iconStyle.SetCornerRadiusAll(12);
        icon.AddThemeStyleboxOverride("panel", iconStyle);
        icon.Position = new Vector2(18, 14);
        icon.Size = new Vector2(44, 44);
        banner.AddChild(icon);
        var iconGlyph = new Label
        {
            Text = "微",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        iconGlyph.AddThemeFontSizeOverride("font_size", 24);
        iconGlyph.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.AddChild(iconGlyph);

        bannerName = new Label { MouseFilter = MouseFilterEnum.Ignore };
        bannerName.Position = new Vector2(78, 12);
        bannerName.AddThemeFontSizeOverride("font_size", 19);
        bannerName.AddThemeColorOverride("font_color", new Color(0.65f, 0.78f, 0.66f));
        banner.AddChild(bannerName);

        bannerText = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        bannerText.Position = new Vector2(78, 40);
        bannerText.Size = new Vector2(920, 34);
        bannerText.AddThemeFontSizeOverride("font_size", 25);
        bannerText.AddThemeColorOverride("font_color", new Color(0.96f, 0.95f, 0.90f));
        banner.AddChild(bannerText);
    }

    // ==================== 掠过物（路牌 / 里程碑 / F19 彩蛋卡） ====================

    public void AddSign(string text, float delay = 0f, float speed = 1.0f)
    {
        var board = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.34f, 0.22f, 0.94f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            BorderColor = new Color(0.85f, 0.87f, 0.85f, 0.9f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
        };
        board.AddThemeStyleboxOverride("panel", style);
        board.MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 0.94f));
        board.AddChild(label);

        SpawnPasser(board, label, new Vector2(230, 64), new Vector2(0.5f, 0.26f), delay, speed);
    }

    public void AddMarker(string text, float delay = 0f, float speed = 1.0f)
    {
        var board = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.82f, 0.80f, 0.74f, 0.92f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        };
        board.AddThemeStyleboxOverride("panel", style);
        board.MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(0.25f, 0.25f, 0.25f));
        board.AddChild(label);

        SpawnPasser(board, label, new Vector2(72, 40), new Vector2(0.5f, 0.50f), delay, speed);
    }

    /// <summary>F19 聊天外彩蛋：暖黄小卡（服务区橘猫 / 货车标语 / FM 电台），从右往左轻轻掠过</summary>
    public void AddFlavor(string text, float delay = 0f, float speed = 1.0f)
    {
        var board = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.96f, 0.90f, 0.72f, 0.94f),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ShadowColor = new Color(0, 0, 0, 0.25f),
            ShadowSize = 6,
        };
        board.AddThemeStyleboxOverride("panel", style);
        board.MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", new Color(0.35f, 0.28f, 0.16f));
        board.AddChild(label);

        SpawnPasser(board, label, new Vector2(190, 92), new Vector2(0.82f, 0.74f), delay, speed);
    }

    private void SpawnPasser(Panel body, Label label, Vector2 size, Vector2 anchor, float delay, float speed)
    {
        body.CustomMinimumSize = size;
        body.Size = size;
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        passingRoot.AddChild(body);

        float startX = GetViewport().GetVisibleRect().Size.X + 40;
        float endX = -size.X - 60;
        float y = GetViewport().GetVisibleRect().Size.Y * anchor.Y;

        body.Position = new Vector2(startX, y);
        body.Modulate = new Color(1, 1, 1, 0);

        var tw = CreateTween();
        tw.TweenInterval(delay);
        tw.TweenProperty(body, "modulate:a", 1.0f, 0.3f);

        body.SetMeta("pass_t", 0.0f);
        body.SetMeta("pass_speed", speed);
        body.SetMeta("pass_x0", startX);
        body.SetMeta("pass_x1", endX);
        body.SetMeta("pass_dur", (startX - endX) / 420.0f * (1f / Mathf.Max(speed, 0.05f)));
        passers.Add(body);
    }

    private readonly List<Panel> passers = new();

    // ==================== 帧循环 ====================

    public override void _Process(double delta)
    {
        for (int i = passers.Count - 1; i >= 0; i--)
        {
            var body = passers[i];
            if (!GodotObject.IsInstanceValid(body) || !body.HasMeta("pass_t"))
            {
                passers.RemoveAt(i);
                continue;
            }

            float t = (float)body.GetMeta("pass_t") + (float)delta / (float)body.GetMeta("pass_dur");
            body.SetMeta("pass_t", t);

            float x0 = (float)body.GetMeta("pass_x0");
            float x1 = (float)body.GetMeta("pass_x1");
            body.Position = new Vector2(Mathf.Lerp(x0, x1, Mathf.Clamp(t, 0f, 1f)), body.Position.Y);

            if (t >= 1f)
            {
                passers.RemoveAt(i);
                body.QueueFree();
            }
        }

        if (running)
        {
            spawnTimer += delta;
            if (spawnTimer > signCountdown)
            {
                spawnTimer = 0;
                signCountdown = rng.Next(5, 10);
                int km = 380 - rng.Next(0, 4);
                AddMarker($"G205 {km}", 0f, 1f);
            }
        }
    }

    // ==================== 雨幕 ====================

    private GpuParticles2D BuildRain()
    {
        var img = Image.CreateEmpty(2, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++)
        {
            float a = Mathf.Sin((y + 0.5f) / 16f * Mathf.Pi) * 0.55f;
            img.SetPixel(0, y, new Color(0.85f, 0.90f, 1.0f, a));
            img.SetPixel(1, y, new Color(0.85f, 0.90f, 1.0f, a));
        }
        var tex = ImageTexture.CreateFromImage(img);

        var p = new GpuParticles2D
        {
            Name = "Rain",
            Emitting = false,
            Amount = 340,
            AmountRatio = 0f,
            Lifetime = 0.9f,
            Preprocess = 0.6f,
            Texture = tex,
            ProcessMaterial = MakeRainMaterial(new Vector2(1150, 20)),
        };
        p.Position = new Vector2(960, -30);
        return p;
    }

    private ParticleProcessMaterial MakeRainMaterial(Vector2 extents)
    {
        var m = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(extents.X, extents.Y, 1),
            Direction = new Vector3(-0.18f, 1f, 0),
            Spread = 2f,
            InitialVelocityMin = 950f,
            InitialVelocityMax = 1250f,
            Gravity = new Vector3(120, 60, 0),
            ScaleMin = 0.8f,
            ScaleMax = 1.5f,
        };
        return m;
    }

    // ==================== 电量 HUD ====================

    private void BuildBatteryHud()
    {
        batteryHud = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        batteryHud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(batteryHud);

        batteryFrame = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.12f, 0.55f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            BorderColor = new Color(0.92f, 0.90f, 0.84f, 0.75f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        };
        batteryFrame.AddThemeStyleboxOverride("panel", style);

        batteryFrame.AnchorLeft = 1f; batteryFrame.AnchorRight = 1f;
        batteryFrame.AnchorTop = 1f; batteryFrame.AnchorBottom = 1f;
        batteryFrame.OffsetLeft = -300; batteryFrame.OffsetTop = -96;
        batteryFrame.OffsetRight = -32; batteryFrame.OffsetBottom = -44;
        batteryHud.AddChild(batteryFrame);

        batteryFill = new ColorRect { Color = new Color(0.72f, 0.78f, 0.62f) };
        batteryFill.AnchorLeft = 0f; batteryFill.AnchorTop = 0f;
        batteryFill.AnchorBottom = 1f; batteryFill.AnchorRight = 0.62f;
        batteryFill.OffsetLeft = 6; batteryFill.OffsetTop = 6;
        batteryFill.OffsetBottom = -6; batteryFill.OffsetRight = -6;
        batteryFrame.AddChild(batteryFill);

        batteryLabel = new Label
        {
            Text = "电量",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        batteryLabel.AddThemeFontSizeOverride("font_size", 18);
        batteryLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.94f, 0.88f));
        batteryLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.09f, 0.12f, 0.9f));
        batteryLabel.AddThemeConstantOverride("outline_size", 6);
        batteryLabel.AnchorLeft = 0f; batteryLabel.AnchorRight = 1f;
        batteryLabel.AnchorTop = 0f; batteryLabel.AnchorBottom = 1f;
        batteryLabel.OffsetLeft = 0; batteryLabel.OffsetRight = -10;
        batteryFrame.AddChild(batteryLabel);
    }

    // ==================== 伪 3D 公路 + 骑手（第二十二轮重做核心） ====================
    //
    // 【画法】
    //   地平线在 42% 高。路面 = 从灭点扇开的深灰梯形；
    //   中央虚线 12 段向下滚动（scrollT 匀速推进 = 前进感）；
    //   路边电线杆 8 根，从远处一个小点涌成近处一根大杆；
    //   骑手剪影在路面下方，←→/A D 左右挪（路跟着反打一点点 = 转弯感）。
    // 【操控】无惩罚无目标：这条路没有对错，只有风。

    private partial class RoadLayer : Control
    {
        public RideTrack G;

        private float scrollT;      // 路面滚动相位（0~1 循环）
        private bool moving;
        private float steer;        // -1..1 骑手横向位置（缓动）
        private float steerInput;   // 按键输入

        // 第三轮 F5：路上的障碍物（纯视觉演出——水洼/树枝/落石/路牌，从远到近掠过）
        private class Obstacle { public float X; public float Z; public int Kind; }
        private readonly System.Collections.Generic.List<Obstacle> obstacles = new();
        private float spawnAcc;
        private readonly System.Random rng = new();
        private readonly Color RoadCol = new(0.22f, 0.22f, 0.24f);
        private readonly Color RoadSide = new(0.30f, 0.31f, 0.27f);
        private readonly Color LineCol = new(0.90f, 0.88f, 0.78f, 0.8f);
        private readonly Color RiderCol = new(0.06f, 0.055f, 0.05f);

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // 轻操控：←→ / A D；松手缓缓回中
            steerInput = 0f;
            if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A))
                steerInput = -1f;
            else if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D))
                steerInput = 1f;
            steer = Mathf.MoveToward(steer, steerInput, dt * (steerInput != 0f ? 1.6f : 0.9f));
            steer = Mathf.Clamp(steer, -1f, 1f);

            if (moving)
            {
                scrollT = (scrollT + dt * 0.55f) % 1f;

                // 障碍物：约每 2.5~4 秒来一个，从地平线滑到眼前
                spawnAcc += dt;
                if (spawnAcc > 2.5f + (float)rng.NextDouble() * 1.5f)
                {
                    spawnAcc = 0f;
                    obstacles.Add(new Obstacle
                    {
                        X = -0.55f + (float)rng.NextDouble() * 1.1f,
                        Z = 0.02f,
                        Kind = rng.Next(4),   // 0 水洼 / 1 树枝 / 2 落石 / 3 路牌
                    });
                }
                for (int i = obstacles.Count - 1; i >= 0; i--)
                {
                    obstacles[i].Z += dt * 0.30f;
                    if (obstacles[i].Z > 1.18f)
                        obstacles.RemoveAt(i);
                }
            }

            QueueRedraw();
        }

        public void SetMoving(bool on) => moving = on;

        public override void _Draw()
        {
            float w = Size.X;
            float h = Size.Y;
            float horizon = h * 0.42f;

            float vanishX = w * 0.5f + steer * w * 0.06f;         // 灭点跟着骑手偏一点（转弯感）
            float riderX = w * 0.5f + steer * w * 0.22f;           // 骑手横向位置
            float roadHalfNear = w * 0.46f;
            float roadHalfFar = w * 0.045f;

            // ---- 路肩大地（草/路肩色，接住底图和路面的过渡） ----
            DrawRect(new Rect2(0, horizon, w, h - horizon), RoadSide);

            // ---- 路面梯形 ----
            DrawColoredPolygon(new[]
            {
                new Vector2(vanishX - roadHalfFar, horizon),
                new Vector2(vanishX + roadHalfFar, horizon),
                new Vector2(w * 0.5f + roadHalfNear, h),
                new Vector2(w * 0.5f - roadHalfNear, h),
            }, RoadCol);

            // ---- 路肩白实线（两条） ----
            for (int side = -1; side <= 1; side += 2)
            {
                float farX = vanishX + side * roadHalfFar;
                float nearX = w * 0.5f + side * roadHalfNear;
                DrawLine(new Vector2(farX, horizon), new Vector2(nearX, h),
                    new Color(0.92f, 0.90f, 0.84f, 0.55f), 3.5f);
            }

            // ---- 中央虚线（12 段滚动） ----
            for (int i = 0; i < 12; i++)
            {
                float t0 = (i / 12f + scrollT) % 1f;
                float t1 = (t0 + 0.045f) % 1f;
                if (t1 < t0) t1 += 1f;
                float y0 = Mathf.Lerp(horizon, h, t0 * t0 * (3f - 2f * t0)); // smoothstep 透视感
                float y1 = Mathf.Lerp(horizon, h, t1 * t1 * (3f - 2f * t1));
                float x0 = Mathf.Lerp(vanishX, w * 0.5f + steer * w * 0.1f, t0);
                float x1 = Mathf.Lerp(vanishX, w * 0.5f + steer * w * 0.1f, t1);
                float lw = Mathf.Lerp(1.5f, 6f, t0);
                DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), LineCol, lw);
            }

            // ---- 路边电线杆（8 根，远处小点涌成大杆；左右交替） ----
            for (int i = 0; i < 8; i++)
            {
                float t = (i / 8f + scrollT * 0.92f) % 1f;
                float y = Mathf.Lerp(horizon, h, t * t * (3f - 2f * t));
                float sideSign = i % 2 == 0 ? -1f : 1f;
                float poleX = Mathf.Lerp(vanishX, w * 0.5f + sideSign * w * 0.52f, t);
                float poleH = Mathf.Lerp(7f, 240f, t * t);
                var poleCol = new Color(0.14f, 0.13f, 0.12f, 0.85f);
                DrawLine(new Vector2(poleX, y), new Vector2(poleX, y - poleH), poleCol, Mathf.Lerp(1.5f, 7f, t));
                // 横臂
                DrawLine(new Vector2(poleX - poleH * 0.12f, y - poleH + poleH * 0.06f),
                    new Vector2(poleX + poleH * 0.12f, y - poleH + poleH * 0.06f), poleCol,
                    Mathf.Lerp(1f, 4.5f, t));
            }

            // ---- 障碍物（第三轮 F5）：从地平线滑过来，逼近时变大、淡出 ----
            foreach (var ob in obstacles)
            {
                float z = ob.Z;
                if (z <= 0f || z >= 1.12f)
                    continue;
                float persp = z * z;                       // 近处加速变大
                float y = horizon + (h * 0.86f - horizon) * persp;
                float halfRoad = Mathf.Lerp(roadHalfFar, roadHalfNear, persp);
                float x = vanishX + ob.X * halfRoad;
                float sc = h / 1080f * (0.25f + persp * 1.5f);
                float alpha = Mathf.Clamp(1.4f - z * 1.3f, 0f, 1f);
                if (alpha <= 0.02f)
                    continue;

                switch (ob.Kind)
                {
                    case 0: // 积水坑：深色扁椭圆 + 一道冷反光
                        DrawSetTransform(new Vector2(x, y), 0f, new Vector2(1f, 0.35f));
                        DrawCircle(Vector2.Zero, 34f * sc, new Color(0.13f, 0.15f, 0.19f, 0.55f * alpha));
                        DrawCircle(Vector2.Zero, 22f * sc, new Color(0.35f, 0.40f, 0.48f, 0.35f * alpha));
                        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                        break;
                    case 1: // 掉落的树枝：两段折线
                        DrawLine(new Vector2(x - 30 * sc, y), new Vector2(x + 8 * sc, y - 4 * sc),
                            new Color(0.18f, 0.14f, 0.10f, 0.8f * alpha), 4f * sc);
                        DrawLine(new Vector2(x + 8 * sc, y - 4 * sc), new Vector2(x + 36 * sc, y - 1 * sc),
                            new Color(0.20f, 0.15f, 0.11f, 0.75f * alpha), 3f * sc);
                        break;
                    case 2: // 落石：两三块小石块
                        DrawCircle(new Vector2(x, y), 11f * sc, new Color(0.32f, 0.32f, 0.33f, 0.85f * alpha));
                        DrawCircle(new Vector2(x + 16 * sc, y + 3 * sc), 7f * sc, new Color(0.28f, 0.28f, 0.30f, 0.8f * alpha));
                        DrawCircle(new Vector2(x - 14 * sc, y + 2 * sc), 5f * sc, new Color(0.30f, 0.30f, 0.31f, 0.7f * alpha));
                        break;
                    case 3: // 路边警示牌：一根杆 + 三角牌
                        DrawLine(new Vector2(x, y), new Vector2(x, y - 52 * sc),
                            new Color(0.30f, 0.28f, 0.24f, 0.9f * alpha), 3f * sc);
                        DrawColoredPolygon(new[]
                        {
                            new Vector2(x, y - 78 * sc),
                            new Vector2(x - 20 * sc, y - 48 * sc),
                            new Vector2(x + 20 * sc, y - 48 * sc),
                        }, new Color(0.85f, 0.70f, 0.30f, 0.85f * alpha));
                        break;
                }
            }

            // ---- 骑手剪影（人 + 电动车，面朝右） ----
            float baseY = h * 0.90f;
            float s = h / 1080f; // 剪影尺寸按 1080p 基准
            var ink = RiderCol;
            float rx = riderX;

            // 车身
            DrawSetTransform(new Vector2(rx, baseY), steer * -0.05f, Vector2.One);
            var body = new Rect2(-46 * s, -34 * s, 92 * s, 20 * s);
            DrawStyleBox(new StyleBoxFlat
            {
                BgColor = ink,
                CornerRadiusTopLeft = 10, CornerRadiusTopRight = 14,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            }, body);
            // 轮子（前后）
            DrawCircle(new Vector2(-32 * s, -6 * s), 15 * s, ink);
            DrawCircle(new Vector2(34 * s, -6 * s), 15 * s, ink);
            DrawCircle(new Vector2(-32 * s, -6 * s), 6 * s, new Color(0.35f, 0.33f, 0.32f));
            DrawCircle(new Vector2(34 * s, -6 * s), 6 * s, new Color(0.35f, 0.33f, 0.32f));
            // 车头把手
            DrawLine(new Vector2(34 * s, -30 * s), new Vector2(46 * s, -58 * s), ink, 5 * s);
            // 人：身体前倾 + 头
            DrawCircle(new Vector2(-6 * s, -96 * s), 15 * s, ink);
            DrawColoredPolygon(new[]
            {
                new Vector2(-20 * s, -34 * s),
                new Vector2(10 * s, -34 * s),
                new Vector2(4 * s, -84 * s),
                new Vector2(-16 * s, -84 * s),
            }, ink);
            // 手臂伸向把手
            DrawLine(new Vector2(-2 * s, -80 * s), new Vector2(42 * s, -56 * s), ink, 7 * s);
            // 腿
            DrawLine(new Vector2(-10 * s, -36 * s), new Vector2(-4 * s, -6 * s), ink, 8 * s);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            // ---- 操作提示（第一次） ----
            if (moving && steer == 0f)
            {
                var font = GetThemeDefaultFont();
                DrawString(font, new Vector2(w * 0.5f - 150, h * 0.965f),
                    "← → 轻轻挪动，风里没有对错", HorizontalAlignment.Left, -1, 19,
                    new Color(0.95f, 0.93f, 0.86f, 0.55f));
            }
        }
    }
}
