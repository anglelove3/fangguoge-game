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
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private AudioStreamPlayer bgmPlayer;

    // 音效播放池（4 个播放器轮流用）
    private readonly List<AudioStreamPlayer> sfxPool = new();
    private int sfxCursor;
    private const int SfxPoolSize = 4;

    // 音效文件路径集中放这里，改声音只改这一个地方
    public const string SfxClick = "res://assets/audio/sfx/ui_click.ogg";     // 推进对话 / 通用点击
    public const string SfxConfirm = "res://assets/audio/sfx/ui_confirm.ogg"; // 选项确定
    public const string SfxHover = "res://assets/audio/sfx/ui_hover.ogg";     // 悬停
    public const string SfxTick = "res://assets/audio/sfx/ui_tick.ogg";       // 打字机

    // 默认背景音乐（DEMO 占位：萨蒂《Gymnopédie No.1》，公有领域录音）
    private const string DefaultBgmPath = "res://assets/audio/bgm/gymnopedie_no1.ogg";

    public override void _Ready()
    {
        Instance = this;

        bgmPlayer = new AudioStreamPlayer();
        bgmPlayer.VolumeDb = -14f; // 背景音乐轻一点，不抢戏
        AddChild(bgmPlayer);

        // 建音效播放池
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            sfxPool.Add(p);
        }

        GD.Print("[音频] 已加载（含音效池）");
        PlayBgm(DefaultBgmPath);
    }

    /// <summary>
    /// 播放背景音乐（已经在放同一首就什么都不做）
    /// </summary>
    public void PlayBgm(string path, float volumeDb = -14f)
    {
        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PrintErr($"[音频] 找不到音乐文件：{path}");
            return;
        }

        if (bgmPlayer.Stream == stream && bgmPlayer.Playing)
            return;

        // 让音乐循环播放（ogg 和 mp3 两种格式都处理）
        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;
        else if (stream is AudioStreamMP3 mp3)
            mp3.Loop = true;

        bgmPlayer.Stream = stream;
        bgmPlayer.VolumeDb = volumeDb;
        bgmPlayer.Play();
        GD.Print($"[音频] 播放背景音乐：{path}");
    }

    /// <summary>停止背景音乐</summary>
    public void StopBgm()
    {
        bgmPlayer?.Stop();
    }

    /// <summary>
    /// 播放一个音效
    /// </summary>
    /// <param name="path">音效文件路径（用 SfxClick 这些常量）</param>
    /// <param name="volumeDb">音量</param>
    /// <param name="pitchJitter">音高随机波动范围，比如 0.06 表示 ±6%（打字机音效错落有致的关键）</param>
    public void PlaySfx(string path, float volumeDb = -10f, float pitchJitter = 0f)
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
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchJitter > 0f
            ? (float)GD.RandRange(1.0 - pitchJitter, 1.0 + pitchJitter)
            : 1.0f;
        player.Play();
    }
}
