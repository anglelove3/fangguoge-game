using Godot;
using System;

/// <summary>
/// 主菜单脚本
/// 挂在 main_menu.tscn 的根节点上
///
/// 【Day 2 知识点 - 信号绑定】
/// Godot 中按钮点击会发出 "pressed" 信号，
/// C# 中用 += 把这个信号连接到我们的方法上。
/// 就像：当门铃响了(pressed)，就去开门(OnXxxPressed)。
///
/// 【第二轮视觉升级 - 氛围层】
/// 除了功能逻辑，这里还负责给主菜单"加点氛围"：
///   - 背景缓慢呼吸（放大缩小），静态图不再死板
///   - 标题光晕明暗脉动，像烛光一样
///   - 暖金浮尘粒子（前后两层，制造景深）
///   - 按钮果冻动效（悬停放大 / 按下缩小）
/// 全部用代码生成，不增加任何美术素材。
/// </summary>
public partial class MainMenu : Control
{
    // 声明继续游戏按钮（可能不存在，取决于有没有存档）
    private Button continueButton;

    public override void _Ready()
    {
        // 获取按钮节点
        var startButton = GetNode<Button>("VBoxContainer/StartButton");
        var settingsButton = GetNode<Button>("VBoxContainer/SettingsButton");
        var quitButton = GetNode<Button>("VBoxContainer/QuitButton");

        // 检查是否有存档，决定"继续游戏"按钮是否可用
        continueButton = GetNodeOrNull<Button>("VBoxContainer/ContinueButton");
        if (continueButton != null)
        {
            if (GameManager.Instance.HasSaveFile())
            {
                continueButton.Visible = true;
                continueButton.Pressed += OnContinueGamePressed;
            }
            else
            {
                continueButton.Visible = false;
            }
        }

        // 绑定按钮事件
        startButton.Pressed += OnStartGamePressed;
        settingsButton.Pressed += OnSettingsPressed;
        quitButton.Pressed += OnQuitPressed;

        // 给所有按钮挂上"悬停/点击"音效
        UiSounds.WireAll(this);

        // 氛围层：光晕、浮尘、按钮果冻
        SetupAtmosphere(startButton, settingsButton, quitButton);

        GD.Print("主菜单已加载");
    }

    // ==================== 氛围层（纯代码生成） ====================

    /// <summary>
    /// 给主菜单铺上氛围：呼吸背景 + 脉动光晕 + 两层浮尘 + 按钮果冻
    /// </summary>
    private void SetupAtmosphere(Button startButton, Button settingsButton, Button quitButton)
    {
        // 1) 背景缓慢呼吸：6 秒放大 3%，再 6 秒缩回，循环
        var bg = GetNode<TextureRect>("Background");
        bg.PivotOffset = new Vector2(960, 540); // 从屏幕中心缩放
        var bgTween = CreateTween().SetLoops();
        bgTween.TweenProperty(bg, "scale", new Vector2(1.03f, 1.03f), 6.0)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        bgTween.TweenProperty(bg, "scale", Vector2.One, 6.0)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // 2) 标题光晕明暗脉动：3.5 秒一个来回
        var halo = GetNode<TextureRect>("TitleHalo");
        var haloTween = CreateTween().SetLoops();
        haloTween.TweenProperty(halo, "modulate:a", 0.55f, 1.75)
                 .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        haloTween.TweenProperty(halo, "modulate:a", 0.95f, 1.75)
                 .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // 3) 暖金浮尘：远处一层（大、慢、淡）+ 近处一层（小、快、亮），制造景深
        GetNode<Node2D>("FxBehind").AddChild(MakeDustLayer(64, 0.05f, 0.2f, 0.5f, -2.5f));
        GetNode<Node2D>("FxFront").AddChild(MakeDustLayer(22, 0.1f, 0.34f, 0.3f, -4.0f));

        // 4) 按钮果冻反馈
        WireButtonJuice(startButton);
        WireButtonJuice(settingsButton);
        WireButtonJuice(quitButton);
        if (continueButton != null && continueButton.Visible)
            WireButtonJuice(continueButton);
    }

    /// <summary>
    /// 生成一层暖金浮尘粒子（向上慢慢飘，像阳光里的灰尘）
    /// </summary>
    /// <param name="amount">粒子数量</param>
    /// <param name="minScale">最小尺寸倍率</param>
    /// <param name="maxScale">最大尺寸倍率</param>
    /// <param name="alpha">整体透明度（远层淡、近层亮）</param>
    /// <param name="gravity">Y 重力（负数 = 往上飘）</param>
    private static CpuParticles2D MakeDustLayer(int amount, float minScale, float maxScale, float alpha, float gravity)
    {
        return new CpuParticles2D
        {
            Name = "WarmDust",
            Amount = amount,
            Lifetime = 9.0,
            Preprocess = 5.0, // 提前"热身"5 秒，进菜单就能看到已有粒子
            Randomness = 0.65f,
            LifetimeRandomness = 0.6f,
            Position = new Vector2(960, 540), // 屏幕中心
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(1020, 600), // 铺满整个画面
            Direction = new Vector2(0, -1),
            Spread = 180.0f, // 全方位散开
            Gravity = new Vector2(0, gravity),
            InitialVelocityMin = 3.0f,
            InitialVelocityMax = 13.0f,
            ScaleAmountMin = minScale,
            ScaleAmountMax = maxScale,
            Color = new Color(1f, 0.89f, 0.68f, alpha),
            Texture = GetDustTexture(),
            ColorRamp = GetDustRamp(),
            // 加法混合：亮部叠加，像发光的尘埃
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            Seed = GD.Randi(),
        };
    }

    /// <summary>浮尘贴图：中间亮、边缘透明的白色小圆（染色靠粒子 Color）</summary>
    private static Texture2D GetDustTexture()
    {
        var gradient = new Gradient();
        gradient.Offsets = new float[] { 0f, 0.4f, 1f };
        gradient.Colors = new Color[]
        {
            Colors.White,
            new Color(1, 1, 1, 0.5f),
            new Color(1, 1, 1, 0),
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }

    /// <summary>粒子生命周期曲线：淡入 → 保持 → 淡出（避免粒子突然出现/消失）</summary>
    private static Gradient GetDustRamp()
    {
        var gradient = new Gradient();
        gradient.Offsets = new float[] { 0f, 0.3f, 0.75f, 1f };
        gradient.Colors = new Color[]
        {
            new Color(1, 1, 1, 0f),
            new Color(1, 1, 1, 1f),
            new Color(1, 1, 1, 0.9f),
            new Color(1, 1, 1, 0f),
        };
        return gradient;
    }

    /// <summary>
    /// 按钮果冻动效：悬停微微放大，按下缩一点点，松开弹回
    /// </summary>
    private static void WireButtonJuice(Button button)
    {
        bool hovered = false;

        button.MouseEntered += () =>
        {
            hovered = true;
            TweenButtonScale(button, 1.04f, 0.12f);
        };
        button.MouseExited += () =>
        {
            hovered = false;
            TweenButtonScale(button, 1.0f, 0.14f);
        };
        button.ButtonDown += () => TweenButtonScale(button, 0.97f, 0.06f);
        button.ButtonUp += () => TweenButtonScale(button, hovered ? 1.04f : 1.0f, 0.1f);
    }

    /// <summary>把按钮缩放到指定倍率（先杀掉上一次动画，避免打架）</summary>
    private static void TweenButtonScale(Button button, float scale, float duration)
    {
        if (button.HasMeta("juice_tw"))
            button.GetMeta("juice_tw").As<Tween>()?.Kill();

        button.PivotOffset = button.Size / 2f; // 从按钮中心缩放
        var tween = button.CreateTween();
        button.SetMeta("juice_tw", tween);
        tween.TweenProperty(button, "scale", new Vector2(scale, scale), duration)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    // ==================== 按钮功能 ====================

    /// <summary>
    /// 点击"开始游戏"→ 重置数据，进入序章
    /// </summary>
    private void OnStartGamePressed()
    {
        GD.Print("→ 开始新游戏");
        GameManager.Instance.ResetGame();
        GameManager.Instance.ChangeSceneWithTransition(
            "res://scenes/chapters/prologue/prologue.tscn",
            chapterIndex: 0
        );
    }

    /// <summary>
    /// 点击"继续游戏"→ 读取存档，进入对应章节
    /// </summary>
    private void OnContinueGamePressed()
    {
        GD.Print("→ 继续游戏");
        bool loaded = GameManager.Instance.LoadGame();
        if (loaded)
        {
            string scenePath = GetChapterScenePath(GameManager.Instance.CurrentChapter);
            GameManager.Instance.ChangeSceneWithTransition(scenePath);
        }
    }

    /// <summary>
    /// 点击"设置"→ 打开设置界面
    /// </summary>
    private void OnSettingsPressed()
    {
        GD.Print("→ 打开设置");
        GameManager.Instance.ChangeSceneWithTransition("res://scenes/ui/settings/settings.tscn");
    }

    /// <summary>
    /// 点击"退出游戏"
    /// </summary>
    private void OnQuitPressed()
    {
        GD.Print("→ 退出游戏");
        GetTree().Quit();
    }

    /// <summary>
    /// 根据章节编号返回对应的场景路径
    /// 后续添加新章节时在这里加一行就行
    /// </summary>
    private string GetChapterScenePath(int chapter)
    {
        return chapter switch
        {
            0 => "res://scenes/chapters/prologue/prologue.tscn",
            1 => "res://scenes/chapters/ch01/ch01.tscn",
            2 => "res://scenes/chapters/ch02/ch02.tscn",
            99 => "res://scenes/ui/demo_end/demo_end.tscn", // 试玩结束页
            // 后续章节在这里添加：
            // 3 => "res://scenes/chapters/ch03/xxx/ch03.tscn",
            _ => "res://scenes/chapters/prologue/prologue.tscn"
        };
    }
}
