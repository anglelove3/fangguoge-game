using System.Collections.Generic;

/// <summary>
/// 手机桌面端的数据（第十五轮）
///
/// 【这是什么】
/// 手机从"打开就是微信"，升级成了一台真正的手机：
/// 锁屏 → 桌面（12 个 App 图标）→ 点开 App。
/// 微信 / 网易云音乐 / 相册 / QQ邮箱 / DeepSeek 五个是可以真的玩的，
/// 剩下七个（抖音、百度、支付宝、美团、小红书、QQ、淘宝）点开各有一句小彩蛋。
///
/// 【数据在哪】
/// data/phone/phone.json —— 桌面图标、歌单、相册、邮箱、DeepSeek 问答，全在里面。
/// 改文案不碰代码，和微信那边一个规矩。
/// </summary>
public static class PhoneData
{
    // ==================== 数据结构 ====================

    public class AppEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Glyph { get; set; } = "";     // 图标里那个字
        public string Color { get; set; } = "";     // 图标底色（#RRGGBB）
        public string Kind { get; set; } = "demo";  // real = 能真的玩；demo = 演示彩蛋
        public bool DarkGlyph { get; set; }         // 浅色图标用深色字
        public List<string> Lines { get; set; } = new(); // 演示版里那几行字
    }

    public class SongEntry
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string File { get; set; } = "";      // 音频路径（留空 = 放不出来）
        public string Cover { get; set; } = "";     // 封面图
        public bool Locked { get; set; }
        public string Hint { get; set; } = "";      // 放不出来时的提示
    }

    public class PhotoEntry
    {
        public string Photo { get; set; } = "";
        public string Caption { get; set; } = "";
    }

    public class MailEntry
    {
        public string From { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Time { get; set; } = "";
        public bool Unread { get; set; }
        public List<string> Body { get; set; } = new();
    }

    public class AiEntry
    {
        public string Q { get; set; } = "";
        public string A { get; set; } = "";
        /// <summary>可选：到达该章节后才显示这条提问（如 "ch03"）</summary>
        public string After { get; set; } = "";
    }

    public class AiSection
    {
        public string Greeting { get; set; } = "";
        public List<AiEntry> Questions { get; set; } = new();
    }

    public class MusicSection
    {
        public List<SongEntry> Songs { get; set; } = new();
    }

    public class PhoneFile
    {
        public List<AppEntry> Apps { get; set; } = new();
        public MusicSection Music { get; set; } = new();
        public List<PhotoEntry> Album { get; set; } = new();
        public List<MailEntry> Mail { get; set; } = new();
        public AiSection Ai { get; set; } = new();
    }

    // ==================== 读取 ====================

    private static PhoneFile data;

    public static PhoneFile Data =>
        data ??= DataStore.LoadJson<PhoneFile>("res://data/phone/phone.json") ?? new PhoneFile();

    /// <summary>找一台 App 的桌面定义</summary>
    public static AppEntry App(string id) => Data.Apps.Find(a => a.Id == id);

    /// <summary>把 "#RRGGBB" 解析成 Color（解析失败给个中性灰）</summary>
    public static Godot.Color ParseColor(string hex)
    {
        return !string.IsNullOrEmpty(hex) ? new Godot.Color(hex) : new Godot.Color(0.5f, 0.5f, 0.55f);
    }
}
