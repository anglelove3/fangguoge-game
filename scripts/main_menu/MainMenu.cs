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
        // 回到标题画面：把音乐换回主题曲（第三章/第四章各有专属 BGM，不切就会一直响着）
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 1.4f);

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

        // 5) 咖啡杯蒸汽：从杯子位置缓缓升起白色半透明雾气
        AddChild(MakeSteamParticles());

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
    /// 咖啡杯蒸汽：从杯口位置缓缓升起的白色半透明雾气
    /// 咖啡杯在背景图左下，杯口大约在 viewport (344, 631)
    /// </summary>
    private static CpuParticles2D MakeSteamParticles()
    {
        // 蒸汽贴图：比浮尘更大、更软的白色圆斑
        var steamGradient = new Gradient();
        steamGradient.Offsets = new float[] { 0f, 0.3f, 0.7f, 1f };
        steamGradient.Colors = new Color[]
        {
            new Color(1, 1, 1, 0.6f),
            new Color(1, 1, 1, 0.35f),
            new Color(1, 1, 1, 0.1f),
            new Color(1, 1, 1, 0f),
        };
        var steamTexture = new GradientTexture2D
        {
            Gradient = steamGradient,
            Width = 128,
            Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };

        // 蒸汽颜色渐变：从白色半透明 → 完全透明
        var steamRamp = new Gradient();
        steamRamp.Offsets = new float[] { 0f, 0.15f, 0.6f, 1f };
        steamRamp.Colors = new Color[]
        {
            new Color(1, 1, 1, 0f),
            new Color(1, 1, 1, 0.22f),
            new Color(1, 1, 1, 0.12f),
            new Color(1, 1, 1, 0f),
        };

        return new CpuParticles2D
        {
            Name = "CoffeeSteam",
            Amount = 10,
            Lifetime = 2.8,
            Preprocess = 2.0, // 提前"热身"，进菜单就能看到蒸汽
            Randomness = 0.5f,
            LifetimeRandomness = 0.4f,
            Position = new Vector2(344, 631), // 杯口位置（viewport 坐标）
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(50, 8), // 窄长的发射区域（杯口宽度）
            Direction = new Vector2(0, -1),
            Spread = 25.0f, // 轻微左右散开
            Gravity = new Vector2(0, -12), // 缓慢上升
            InitialVelocityMin = 8.0f,
            InitialVelocityMax = 18.0f,
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 1.2f,
            AngularVelocityMin = -15f,
            AngularVelocityMax = 15f,
            Color = Colors.White,
            Texture = steamTexture,
            ColorRamp = steamRamp,
            // 正常混合（不用 Add，蒸汽是半透明白色而非发光）
            Seed = GD.Randi(),
        };
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
    /// 点击"开始游戏"→ 备份旧存档 + 重置数据，进入序章
    ///
    /// 【第十一轮改动（试玩反馈 A1）】
    /// 原来一点"开始游戏"就当场 ResetGame()，上一局进度连个招呼都不打就没了。
    /// 现在：有存档 → 先弹二次确认，同意后把旧档备份成 savegame_prev.json 再清零。
    /// </summary>
    private void OnStartGamePressed()
    {
        if (GameManager.Instance.HasSaveFile())
        {
            ConfirmHost.Ask("confirm.newgame_title", "confirm.newgame_body",
                StartNewGame, okKey: "confirm.newgame_yes", cancelKey: "confirm.newgame_no");
            return;
        }

        StartNewGame();
    }

    /// <summary>真正开始新的一局（确认框点"重新开始"之后走这里）</summary>
    private void StartNewGame()
    {
        GD.Print("→ 开始新游戏");
        GameManager.Instance.BeginNewGame();
        GameManager.Instance.ChangeSceneWithTransition(
            "res://scenes/chapters/prologue/prologue.tscn",
            chapterIndex: 0
        );
    }

    /// <summary>
    /// 点击"继续游戏"→ 读取存档，进入对应章节
    ///
    /// 【第十一轮改动（试玩反馈 C2）】
    /// 存档里的章节号可能是 99（已经通关到结束页），直接跳过去只会看到"感谢试玩"，
    /// 相当于这一局再也续不上。现在把 99 翻译成"重玩最后一章"，至少还有东西可玩。
    /// （r13：第四→第五→第六章接成链路；r14：第七章上线；r17：第八章《还想再见》上线，重玩落点后移到第八章）
    /// </summary>
    private void OnContinueGamePressed()
    {
        GD.Print("→ 继续游戏");
        bool loaded = GameManager.Instance.LoadGame();
        if (!loaded)
            return;

        int chapter = GameManager.Instance.CurrentChapter;
        if (chapter >= 99)
        {
            GD.Print("[继续游戏] 上次已经玩到结束页，改为从第八章继续");
            chapter = LastChapter;
            GameManager.Instance.CurrentChapter = chapter;
        }

        GameManager.Instance.ChangeSceneWithTransition(GetChapterScenePath(chapter));
    }

    /// <summary>目前开发到的最后一章（通关档的"重玩落点"）</summary>
    private const int LastChapter = 8;

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
            3 => "res://scenes/chapters/ch03/ch03.tscn",
            4 => "res://scenes/chapters/ch04/ch04.tscn",
            5 => "res://scenes/chapters/ch05/ch05.tscn",
            6 => "res://scenes/chapters/ch06/ch06.tscn",
            7 => "res://scenes/chapters/ch07/ch07.tscn",
            8 => "res://scenes/chapters/ch08/ch08.tscn",
            99 => "res://scenes/ui/demo_end/demo_end.tscn", // 试玩结束页
            // 后续章节在这里添加：
            _ => "res://scenes/chapters/prologue/prologue.tscn"
        };
    }
}
