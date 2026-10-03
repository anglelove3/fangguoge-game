using Godot;

/// <summary>
/// 音频管理器（自动加载单例）
/// 负责背景音乐：游戏一启动就播放，切换场景不会中断
///
/// 【知识点 - 自动加载单例的好处】
/// BGM 播放器挂在自动加载节点上，切场景时不会被销毁，
/// 所以音乐可以跨章节连续播放，不需要每个场景各自管理。
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private AudioStreamPlayer bgmPlayer;

    // 默认背景音乐（DEMO 占位：萨蒂《Gymnopédie No.1》，公有领域录音）
    private const string DefaultBgmPath = "res://assets/audio/bgm/gymnopedie_no1.ogg";

    public override void _Ready()
    {
        Instance = this;

        bgmPlayer = new AudioStreamPlayer();
        bgmPlayer.VolumeDb = -14f; // 背景音乐轻一点，不抢戏
        AddChild(bgmPlayer);

        GD.Print("[音频] 已加载");
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
}
