using Godot;
using System.Collections.Generic;

/// <summary>
/// 公路滚行演出层（第十章《向南》招牌组件）
///
/// 【设计定位】玩家不操控车——这一层只负责把"他正在往南骑"演出来：
///   1. 背景缓慢漂移（水彩图整体轻摆，行进起伏感，不需要循环接缝）
///   2. 掠过物（路牌 / 里程碑）从右往左滑过屏幕——程序绘制的 UI 物件，零新素材
///   3. HUD 电量条：电量是剧情脚本的演出元素，不是系统——章节脚本说掉多少就掉多少
///   4. 雨幕粒子 + 时段调色层（清晨灰蓝 / 雨天暗青 / 黄昏烧红，同一张底图三种天光）
///
/// 【与章节脚本的关系】Ch10 是导演，RideTrack 是舞台：
///   Start()/Stop() 行进开关；SetTint("morning"/"rain"/"dusk") 切天光；
///   AddSign()/AddMarker() 安排掠过物；SetBattery() 摆电量；PauseForChoice()/Resume() 停车答题。
/// </summary>
public partial class RideTrack : Control
{
    // ==================== 节点 ====================

    private TextureRect bg;            // 公路底图（超屏，漂移用）
    private Control passingRoot;       // 掠过物容器
    private ColorRect darken;          // 压暗层（所有天光共用）
    private ColorRect tintLayer;       // 天光染色层
    private GpuParticles2D rain;       // 雨幕
    private AudioStreamPlayer engine;  // 电机声循环

    // 电量 HUD
    private Control batteryHud;
    private Panel batteryFrame;
    private ColorRect batteryFill;
    private Label batteryLabel;

    // ==================== 状态 ====================

    private bool running;          // 行进中（背景漂移 + 掠过物生成）
    private float driftPhase;      // 背景漂移相位
    private double spawnTimer;     // 掠过物生成计时
    private float passSpeed = 1.0f; // 掠过速度倍率（停车时降到 0）
    private float speedTarget = 1.0f;
    private double signCountdown;  // 下一块路牌的间隔
    private readonly List<(Panel body, Label text, float speed)> passers = new();

    private System.Random rng = new();

    // ==================== 天光配方 ====================

    private static readonly System.Collections.Generic.Dictionary<string, (Color tint, Color dark)> Tints =
        new()
        {
            // tint = 染色层颜色；dark = 压暗层颜色（a 通道即浓度）
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

        // —— 底图：超屏 12%，行进时左右轻摆 ——
        bg = new TextureRect
        {
            Name = "RoadBg",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.OffsetLeft = -110;
        bg.OffsetRight = 110;
        AddChild(bg);

        // —— 天光两层 ——
        tintLayer = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.Transparent };
        darken = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.Transparent };
        foreach (var layer in new Control[] { tintLayer, darken })
        {
            layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(layer);
        }

        // —— 掠过物容器 ——
        passingRoot = new Control { MouseFilter = MouseFilterEnum.Ignore };
        passingRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(passingRoot);

        // —— 雨幕 ——
        rain = BuildRain();
        AddChild(rain);

        // —— 电量 HUD ——
        BuildBatteryHud();

        // —— 电机声（流由章节脚本给；这里只管淡入淡出） ——
        engine = new AudioStreamPlayer { VolumeDb = -60f };
        AddChild(engine);
    }

    /// <summary>底图贴图由章节脚本给（ch10.tscn 也可以直接配，这里兜底）</summary>
    public void SetBackground(Texture2D tex) => bg.Texture = tex;

    /// <summary>电机声流（章节脚本把 AudioStreamWav 给进来，内部转循环）</summary>
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

    // ==================== 行进控制 ====================

    /// <summary>出发（淡入电机声，开始漂移与掠过物）</summary>
    public void Start(float engineDb = -14f)
    {
        running = true;
        speedTarget = 1f;
        if (engine.Stream != null)
        {
            if (!engine.Playing)
                engine.Play();
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", engineDb, 2.0f);
        }
    }

    /// <summary>停车答题（电机声淡下去，掠过物减速停住）</summary>
    public void PauseForChoice()
    {
        speedTarget = 0f;
        if (engine.Playing)
        {
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", -34f, 0.8f);
        }
    }

    /// <summary>答完题继续骑</summary>
    public void Resume(float engineDb = -14f)
    {
        speedTarget = 1f;
        if (engine.Stream != null)
        {
            if (!engine.Playing)
                engine.Play();
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", engineDb, 1.2f);
        }
    }

    /// <summary>彻底收工（进店 / 到站 / 出章）——电机声淡出，掠过物清场</summary>
    public void Stop(float fade = 1.4f)
    {
        running = false;
        speedTarget = 0f;
        if (engine.Playing)
        {
            var tw = CreateTween();
            tw.TweenProperty(engine, "volume_db", -60f, fade);
            tw.TweenCallback(Callable.From(() => engine.Stop())).SetDelay(fade + 0.1f);
        }
    }

    // ==================== 天光 / 雨 / 电量 ====================

    /// <summary>切天光（"morning" / "noon" / "rain" / "dusk" / "night"），1.6 秒缓变</summary>
    public void SetTint(string key)
    {
        if (!Tints.TryGetValue(key, out var recipe))
            return;
        var tw = CreateTween().SetParallel(true);
        tw.TweenProperty(tintLayer, "color", recipe.tint, 1.6f);
        tw.TweenProperty(darken, "color", recipe.dark, 1.6f);
    }

    /// <summary>雨幕开关</summary>
    public void SetRain(bool on, float fade = 1.2f)
    {
        rain.Emitting = on;
        // 雨幕浓度用 amount_ratio 缓变（Godot 4.7 支持）
        var tw = CreateTween();
        tw.TweenProperty(rain, "amount_ratio", on ? 1.0f : 0.0f, fade);
    }

    /// <summary>电量 0~100。颜色跟着分档：稳 / 紧 / 红</summary>
    public void SetBattery(float percent, string text = "")
    {
        batteryHud.Visible = true;
        percent = Mathf.Clamp(percent, 0f, 100f);
        batteryFill.AnchorRight = percent / 100f;

        var c = percent > 30f
            ? new Color(0.72f, 0.78f, 0.62f)      // 灰绿——还稳
            : percent > 12f
                ? new Color(0.90f, 0.68f, 0.38f)  // 橙——紧了
                : new Color(0.85f, 0.36f, 0.30f); // 红——赌命
        batteryFill.Color = c;
        batteryLabel.Text = string.IsNullOrEmpty(text) ? $"电量 {percent:0}%" : text;
    }

    /// <summary>收掉电量 HUD（进店吃饭 / 到站下车时）</summary>
    public void HideBattery() => batteryHud.Visible = false;

    // ==================== 掠过物 ====================

    /// <summary>一块绿底白字路牌从右往左掠过（delay 秒后出发）</summary>
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

        SpawnPasser(board, label, new Vector2(230, 64), new Vector2(0.5f, 0.30f), delay, speed);
    }

    /// <summary>一块里程碑小柱从右往左掠过</summary>
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

        SpawnPasser(board, label, new Vector2(72, 40), new Vector2(0.5f, 0.62f), delay, speed);
    }

    /// <summary>把一个掠过物放进容器：从屏幕右侧外滑到左侧外，滑完自己删自己</summary>
    private void SpawnPasser(Panel body, Label label, Vector2 size, Vector2 anchorY, float delay, float speed)
    {
        body.CustomMinimumSize = size;
        body.Size = size;
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        passingRoot.AddChild(body);

        float startX = GetViewport().GetVisibleRect().Size.X + 40;
        float endX = -size.X - 60;
        float y = GetViewport().GetVisibleRect().Size.Y * anchorY.Y;

        body.Position = new Vector2(startX, y);
        body.Modulate = new Color(1, 1, 1, 0);

        passers.Add((body, label, speed));

        var tw = CreateTween();
        tw.TweenInterval(delay);
        tw.TweenProperty(body, "modulate:a", 1.0f, 0.3f);
        // 滑行推进与自删都在 _Process 里按 meta 做（passSpeed 实时缩放，停车时减速停住）
        body.SetMeta("pass_t", 0.0f);
        body.SetMeta("pass_speed", speed);
        body.SetMeta("pass_x0", startX);
        body.SetMeta("pass_x1", endX);
        body.SetMeta("pass_dur", (startX - endX) / 420.0f * (1f / Mathf.Max(speed, 0.05f)));
    }

    // ==================== 帧循环 ====================

    public override void _Process(double delta)
    {
        // 速度缓变（停车/起步的惯性）
        passSpeed = Mathf.MoveToward(passSpeed, speedTarget, (float)delta * 0.8f);

        // —— 背景漂移：行进时轻摆，停车时定住 ——
        if (running)
        {
            driftPhase += (float)delta * 0.35f * Mathf.Max(passSpeed, 0.15f);
            float drift = Mathf.Sin(driftPhase) * 70f;
            bg.OffsetLeft = -110 + drift;
            bg.OffsetRight = 110 + drift;
        }

        // —— 掠过物推进（t 到头自删） ——
        for (int i = passers.Count - 1; i >= 0; i--)
        {
            var (body, _, _) = passers[i];
            if (!GodotObject.IsInstanceValid(body) || !body.HasMeta("pass_t"))
            {
                passers.RemoveAt(i);
                continue;
            }

            float t = (float)body.GetMeta("pass_t") + (float)delta / (float)body.GetMeta("pass_dur") * passSpeed;
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

        // —— 自动生成零散掠过物（里程碑为主，行进中每 5~9 秒一块） ——
        if (running)
        {
            spawnTimer += delta * passSpeed;
            if (spawnTimer > signCountdown)
            {
                spawnTimer = 0;
                signCountdown = rng.Next(5, 10);
                int km = 380 - rng.Next(0, 4);
                AddMarker($"G205 {km}", 0f, 1f);
            }
        }
    }

    // ==================== 程序构建小件 ====================

    /// <summary>雨幕：顶部横排发射，斜向下落的长条雨丝</summary>
    private GpuParticles2D BuildRain()
    {
        // 雨丝纹理：2x16 的白色细条，两端渐隐
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

    /// <summary>电量 HUD：右下角电池外框 + 填充 + 文案</summary>
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
}
