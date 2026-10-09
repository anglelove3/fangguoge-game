using Godot;
using System.Collections.Generic;

/// <summary>
/// 音频管理器（自动加载单例）
/// 负责背景音乐 + 各种音效：游戏一启动就开始工作，切换场景不中断
///
/// 【知识点 - 自动加载单例的好处】
/// 播放器挂在自动加载节点上，切场景时不会被销毁，
/// 所以音乐可以跨章节连续播放，不需要每个场景各自管理。
///
/// 【知识点 - 音效播放池】
/// 音效可能同时响好几个（打字机 + 点击），
/// 所以准备一小组播放器轮流用（轮盘制），
/// 谁空闲用谁，避免"一句话只配一个播放器，新音效掐掉旧音效"。
///
/// 【第十一轮新增】
///   1) 音量受设置页控制：玩家设置的 音乐/音效 音量会换算成分贝叠加上去；
///   2) 换 BGM 支持交叉淡入淡出（双播放器轮流上），第三章/第四章不再共用一首；
///   3) 补了 6 个新音效（卡片飞入、放上天平、天平吱呀、金属轻碰、键盘、柔和确定）。
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private AudioStreamPlayer bgmA;
    private AudioStreamPlayer bgmB;      // 交叉淡入淡出用的第二个播放器
    private AudioStreamPlayer bgmActive; // 当前正在响的那个
    private Tween bgmTween;

    // 音效播放池（6 个播放器轮流用；第十一轮加了新音效，4 个容易互相掐断）
    private readonly List<AudioStreamPlayer> sfxPool = new();
    private int sfxCursor;
    private const int SfxPoolSize = 6;

    // 音效文件路径集中放这里，改声音只改这一个地方
    public const string SfxClick = "res://assets/audio/sfx/ui_click.ogg";           // 推进对话 / 通用点击
    public const string SfxConfirm = "res://assets/audio/sfx/ui_confirm.ogg";       // 选项确定
    public const string SfxHover = "res://assets/audio/sfx/ui_hover.ogg";           // 悬停
    public const string SfxTick = "res://assets/audio/sfx/ui_tick.ogg";             // 打字机
    public const string SfxCardFly = "res://assets/audio/sfx/sfx_card_fly.ogg";     // 记忆卡片飞出去（翻书声）
    public const string SfxItemPlace = "res://assets/audio/sfx/sfx_item_place.ogg"; // 东西落到托盘上
    public const string SfxScaleCreak = "res://assets/audio/sfx/sfx_scale_creak.ogg"; // 天平受力吱呀
    public const string SfxMetalTap = "res://assets/audio/sfx/sfx_metal_tap.ogg";   // 金属轻碰（碰托盘）
    public const string SfxKeyboard = "res://assets/audio/sfx/sfx_keyboard.ogg";    // 幽灵打字的键盘声
    public const string SfxConfirmSoft = "res://assets/audio/sfx/sfx_confirm_soft.ogg"; // 轻一点的确认（放补偿物）
    public const string SfxPour = "res://assets/audio/sfx/sfx_pour.wav";            // 倒酒（第六章）
    public const string SfxClink = "res://assets/audio/sfx/sfx_clink.wav";          // 碰杯（第六章）
    public const string SfxBallClack = "res://assets/audio/sfx/sfx_ball_clack.wav";     // 台球撞击（第七章）
    public const string SfxPocketDrop = "res://assets/audio/sfx/sfx_pocket_drop.wav";   // 球落袋（第七章）
    public const string SfxMahjongLoop = "res://assets/audio/sfx/sfx_mahjong_loop.wav"; // 麻将声无缝循环（第七章）

    // 背景音乐（萨蒂《吉姆诺佩蒂》三部曲：公有领域 / CC BY 3.0 录音，详见 音乐说明.txt）
    public const string BgmGym1 = "res://assets/audio/bgm/gymnopedie_no1.ogg"; // 序章 / 菜单 / 天亮
    public const string BgmGym2 = "res://assets/audio/bgm/gymnopedie_no2.ogg"; // 第三章《天平》
    public const string BgmGym3 = "res://assets/audio/bgm/gymnopedie_no3.ogg"; // 第四章《手滑》深夜

    /// <summary>当前 BGM 的"基础音量"（不含设置页偏移），设置页改音量时要用它重算</summary>
    private float bgmBaseDb = -14f;

    /// <summary>
    /// BGM 让位偏移（第十五轮）：手机里的音乐 App 放歌时压到 -60dB（听不见），
    /// 暂停 / 收起手机再调回 0——不用记住章节 BGM 是哪首，还的时候自然还原。
    /// </summary>
    private float bgmDuckDb;

    // 默认背景音乐
    private const string DefaultBgmPath = BgmGym1;

    public override void _Ready()
    {
        Instance = this;

        bgmA = new AudioStreamPlayer();
        bgmB = new AudioStreamPlayer();
        AddChild(bgmA);
        AddChild(bgmB);
        bgmActive = bgmA;

        // 建音效播放池
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            sfxPool.Add(p);
        }

        GD.Print("[音频] 已加载（双 BGM 播放器 + 音效池）");
        PlayBgm(DefaultBgmPath);
    }

    /// <summary>玩家设置的音量（分贝偏移），设置没加载时用 0（不影响默认音量）</summary>
    private static float MusicVolDb => GameSettings.Instance?.MusicDb(0f) ?? 0f;
    private static float SfxVolDb => GameSettings.Instance?.SfxDb(0f) ?? 0f;

    /// <summary>
    /// 播放背景音乐（已经在放同一首就什么都不做）
    /// </summary>
    /// <param name="path">音乐文件路径</param>
    /// <param name="volumeDb">基础音量（还会再叠加设置页的音乐音量）</param>
    /// <param name="fadeSeconds">交叉淡入淡出时长，0 = 立刻切</param>
    public void PlayBgm(string path, float volumeDb = -14f, float fadeSeconds = 0f)
    {
        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PrintErr($"[音频] 找不到音乐文件：{path}");
            return;
        }

        if (bgmActive?.Stream == stream && bgmActive?.Playing == true)
        {
            bgmBaseDb = volumeDb;
            bgmActive.VolumeDb = volumeDb + MusicVolDb + bgmDuckDb;
            return;
        }

        // 让音乐循环播放（ogg 和 mp3 两种格式都处理）
        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;
        else if (stream is AudioStreamMP3 mp3)
            mp3.Loop = true;

        float targetDb = volumeDb + MusicVolDb + bgmDuckDb;
        bgmBaseDb = volumeDb;

        // 不淡入淡出：直接换
        if (fadeSeconds <= 0f)
        {
            bgmTween?.Kill();
            bgmB.Stop();
            bgmA.Stop();
            bgmActive = bgmA;
            bgmA.Stream = stream;
            bgmA.VolumeDb = targetDb;
            bgmA.Play();
            GD.Print($"[音频] 播放背景音乐：{path}");
            return;
        }

        // 交叉淡入淡出：另一个播放器从静音淡入，当前这个淡出后停掉
        var incoming = bgmActive == bgmA ? bgmB : bgmA;
        var outgoing = bgmActive;

        bgmTween?.Kill();
        incoming.Stream = stream;
        incoming.VolumeDb = -60f;
        incoming.Play();
        bgmActive = incoming;

        bgmTween = CreateTween();
        bgmTween.SetParallel(true);
        bgmTween.TweenProperty(incoming, "volume_db", targetDb, fadeSeconds);
        if (outgoing != null && outgoing.Playing)
        {
            bgmTween.TweenProperty(outgoing, "volume_db", -60f, fadeSeconds);
            bgmTween.Chain().TweenCallback(Callable.From(outgoing.Stop));
        }
        GD.Print($"[音频] 淡入背景音乐：{path}（{fadeSeconds}s 交叉）");
    }

    /// <summary>停止背景音乐（两个播放器都停）</summary>
    public void StopBgm()
    {
        bgmA?.Stop();
        bgmB?.Stop();
    }

    /// <summary>设置页改了音量时调用：立刻作用到正在放的音乐上</summary>
    public void RefreshVolumes()
    {
        if (bgmActive != null && bgmActive.Playing)
            bgmActive.VolumeDb = bgmBaseDb + MusicVolDb + bgmDuckDb;
    }

    /// <summary>
    /// BGM 让位（第十五轮·手机音乐 App 用）：传 -60 就压到听不见，传 0 就还原。
    /// 用"偏移"而不是停播，是为了不动章节正在放的那首——还的时候什么都不用重新指定。
    /// </summary>
    public void SetBgmDuck(float dbOffset)
    {
        if (Mathf.IsEqualApprox(bgmDuckDb, dbOffset))
            return;
        bgmDuckDb = dbOffset;
        RefreshVolumes();
    }

    /// <summary>
    /// 播放一个音效
    /// </summary>
    /// <param name="path">音效文件路径（用 SfxClick 这些常量）</param>
    /// <param name="volumeDb">音量（还会再叠加设置页的音效音量）</param>
    /// <param name="pitchJitter">音高随机波动范围，比如 0.06 表示 ±6%（打字机音效错落有致的关键）</param>
    /// <param name="pitchBase">基准音高（默认 1.0；倒酒的酒线声想整体偏高就传 1.12 这种）</param>
    public void PlaySfx(string path, float volumeDb = -10f, float pitchJitter = 0f, float pitchBase = 1f)
    {
        if (sfxPool.Count == 0)
            return;

        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PrintErr($"[音频] 找不到音效文件：{path}");
            return;
        }

        // 轮盘制：依次用下一个播放器，不会掐掉刚播放的音效
        var player = sfxPool[sfxCursor];
        sfxCursor = (sfxCursor + 1) % sfxPool.Count;

        player.Stream = stream;
        player.VolumeDb = volumeDb + SfxVolDb;
        float pitch = pitchBase;
        if (pitchJitter > 0f)
            pitch = (float)GD.RandRange(pitchBase - pitchJitter, pitchBase + pitchJitter);
        // 兜底：pitch_scale 必须 > 0，越界会让引擎每帧刷 ERROR（r13 run3 抓到过）
        player.PitchScale = Mathf.Clamp(pitch, 0.05f, 4f);
        player.Play();
    }
}
