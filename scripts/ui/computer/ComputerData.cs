using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// 序章电脑的数据（第十五轮）
///
/// 【这是什么】
/// 序章里那台"陪我熬过的夜比床还多"的实验室电脑，从一句旁白
/// 变成了一台能真的开机用的机器：开关机、桌面图标、我的电脑、
/// 测试数据、论文、邮箱、回收站，全都能点开看。
///
/// 【数据在哪】
///   data/computer/computer.json —— 开机自检、桌面、文件夹、邮箱，全在里面。
///   改文案不碰代码，和手机那套一个规矩。
/// </summary>
public static class ComputerData
{
    // ==================== 数据结构 ====================

    /// <summary>桌面上的一个图标</summary>
    public class DesktopEntry
    {
        public string Type { get; set; } = "folder"; // mycomputer | folder | mail | file
        public string Group { get; set; } = "";      // folder 类型打开哪个分组
        public string FileId { get; set; } = "";     // file 类型直接打开哪个文件
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "folder"; // 画哪种小图标
    }

    /// <summary>一个文件（数据/论文/回收站里都是它）</summary>
    public class FileEntry
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";      // data | paper | recycle | desktop
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "text";   // doc | chart | table | photo | text | audio
        public string Title { get; set; } = "";      // 打开后的大标题
        public List<string> Lines { get; set; } = new();   // 正文/说明行
        public List<List<string>> Rows { get; set; } = new(); // 表格（第一行是表头）
        public string Annotation { get; set; } = ""; // 红笔批注（论文用）
        public string Note { get; set; } = "";       // 灰色心声行
    }

    /// <summary>邮箱里的一封邮件</summary>
    public class MailEntry
    {
        public string From { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Time { get; set; } = "";
        public bool Unread { get; set; }
        public List<string> Body { get; set; } = new();
        public string Note { get; set; } = "";
    }

    /// <summary>我的电脑里的一个盘</summary>
    public class DriveEntry
    {
        public string Name { get; set; } = "";
        public int Percent { get; set; }
        public string Hint { get; set; } = "";
        public bool Warn { get; set; }
    }

    public class ComputerFile
    {
        public string Brand { get; set; } = "";

        [JsonPropertyName("spec_line")]
        public string SpecLine { get; set; } = "";

        [JsonPropertyName("boot_lines")]
        public List<string> BootLines { get; set; } = new();

        [JsonPropertyName("sticky_note")]
        public string StickyNote { get; set; } = "";  // 桌面上那张便利贴

        [JsonPropertyName("mycomputer_note")]
        public string MyComputerNote { get; set; } = "";

        [JsonPropertyName("group_titles")]
        public Dictionary<string, string> GroupTitles { get; set; } = new();

        public List<DriveEntry> Drives { get; set; } = new();
        public List<DesktopEntry> Desktop { get; set; } = new();
        public List<FileEntry> Files { get; set; } = new();
        public List<MailEntry> Mail { get; set; } = new();
    }

    // ==================== 读取 ====================

    private static ComputerFile data;

    public static ComputerFile Data =>
        data ??= DataStore.LoadJson<ComputerFile>("res://data/computer/computer.json") ?? new ComputerFile();

    /// <summary>按 id 找一个文件</summary>
    public static FileEntry File(string id) => Data.Files.Find(f => f.Id == id);

    /// <summary>某个分组里的文件</summary>
    public static List<FileEntry> FilesIn(string group) => Data.Files.FindAll(f => f.Group == group);
}
