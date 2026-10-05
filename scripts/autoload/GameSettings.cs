using Godot;
using System;

/// <summary>
/// 游戏设置（自动加载单例）—— 负责把玩家调好的设置存到硬盘、下次开机自动恢复
///
/// 【知识点 - ConfigFile（Godot 自带的配置文件工具）】
/// 不用自己手写 JSON：
///   cfg.Save("user://settings.cfg")  → 写文件
///   cfg.Load(path)                   → 读文件（文件不存在会返回错误，我们忽略即可）
/// user:// 指向用户的存档目录（和 savegame.json 同一个文件夹）。
///
/// 【为什么数值用 0~1】
/// 界面上滑条是 0~1，落到音频上换算成分贝（dB）：
///   Mathf.LinearToDb(0) = 负无穷（静音），LinearToDb(1) = 0dB（原音量）。
/// 这样"音量拉到底"才是真的没声音，而不是"很小声"。
/// </summary>
public partial class GameSettings : Node
{
    public static GameSettings Instance { get; private set; }

    private const string ConfigPath = "user://settings.cfg";
    private const string Section = "settings";

    // ---------- 可调项（0~1） ----------

    /// <summary>音乐音量（默认 0.7：原 DEMO 的 BGM 音量偏大，压一点更好听）</summary>
    public float MusicVolume { get; set; } = 0.7f;

    /// <summary>音效音量</summary>
    public float SfxVolume { get; set; } = 1.0f;

    /// <summary>文字速度（0 = 很慢，1 = 很快）→ 每秒 12~60 个字</summary>
    public float TextSpeed { get; set; } = 0.45f;

    /// <summary>自动播放停留速度（0 = 慢读，1 = 快读）</summary>
    public float AutoSpeed { get; set; } = 0.5f;

    /// <summary>全屏</summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>文字速度换算成"每秒几个字"（打字机用）</summary>
    public float CharsPerSecond => Mathf.Lerp(12f, 60f, Mathf.Clamp(TextSpeed, 0f, 1f));

    /// <summary>自动播放：读完一句该停多久（按字数估算，再乘上玩家的快慢偏好）</summary>
    public double AutoAdvanceDelay(int charCount)
    {
        float perChar = Mathf.Lerp(0.16f, 0.05f, Mathf.Clamp(AutoSpeed, 0f, 1f));
        return Mathf.Clamp(0.9 + charCount * perChar, 1.2, 8.0);
    }

    /// <summary>音乐音量对应的分贝（BGM 基础音量 -14dB，再乘上玩家设置）</summary>
    public float MusicDb(float baseDb = -14f) => baseDb + Mathf.LinearToDb(Mathf.Max(0.0001f, MusicVolume));

    /// <summary>音效音量对应的分贝</summary>
    public float SfxDb(float baseDb = -10f) => baseDb + Mathf.LinearToDb(Mathf.Max(0.0001f, SfxVolume));

    public override void _Ready()
    {
        Instance = this;
        Load();
        ApplyWindowMode();
        GD.Print($"[设置] 已加载：音乐 {MusicVolume:F2} / 音效 {SfxVolume:F2} / 语速 {TextSpeed:F2}");
    }

    /// <summary>从硬盘读设置（第一次运行没有文件 = 用默认值）</summary>
    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok)
            return;

        MusicVolume = (float)cfg.GetValue(Section, "music", MusicVolume);
        SfxVolume = (float)cfg.GetValue(Section, "sfx", SfxVolume);
        TextSpeed = (float)cfg.GetValue(Section, "text_speed", TextSpeed);
        AutoSpeed = (float)cfg.GetValue(Section, "auto_speed", AutoSpeed);
        Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", Fullscreen);
    }

    /// <summary>把当前设置写回硬盘（设置页每次改动都会调用）</summary>
    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "music", MusicVolume);
        cfg.SetValue(Section, "sfx", SfxVolume);
        cfg.SetValue(Section, "text_speed", TextSpeed);
        cfg.SetValue(Section, "auto_speed", AutoSpeed);
        cfg.SetValue(Section, "fullscreen", Fullscreen);

        var err = cfg.Save(ConfigPath);
        if (err != Error.Ok)
            GD.PrintErr($"[设置] 保存失败：{err}");
    }

    /// <summary>应用窗口模式（启动时 + 设置页切换时）</summary>
    public void ApplyWindowMode()
    {
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }
}
