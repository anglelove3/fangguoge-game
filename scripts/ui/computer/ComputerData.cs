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

    // ==================== 文件系统运行时状态（第二十四轮 F31） ====================
    // 回收站不再是摆设：文件能删进回收站、能还原、能清空。
    // 状态记在本局内存里（关机重开电脑会"恢复出厂"——那台电脑的记忆和男主一样不可靠，反而贴题）。

    /// <summary>被玩家删进回收站的文件 id（不含 JSON 里预置在 recycle 组的旧文件）</summary>
    public static System.Collections.Generic.List<string> RecycleIds { get; } = new();

    /// <summary>清空回收站后彻底消失的文件 id</summary>
    public static System.Collections.Generic.HashSet<string> PurgedIds { get; } = new();

    /// <summary>我的电脑里新建的文件夹名</summary>
    public static System.Collections.Generic.List<string> CustomFolders { get; } = new();

    /// <summary>某个分组里现在能看到的文件（过滤已删除/已清空/已挪去回收站的）</summary>
    public static List<FileEntry> FilesIn(string group)
    {
        var list = Data.Files.FindAll(f => f.Group == group
            && !PurgedIds.Contains(f.Id)
            && !(group != "recycle" && RecycleIds.Contains(f.Id)));
        if (group == "recycle")
        {
            foreach (var id in RecycleIds)
            {
                var f = File(id);
                if (f != null && !list.Contains(f))
                    list.Add(f);
            }
        }
        return list;
    }

    /// <summary>删进回收站（从原分组消失）</summary>
    public static void MoveToRecycle(string fileId)
    {
        if (!string.IsNullOrEmpty(fileId) && !RecycleIds.Contains(fileId))
            RecycleIds.Add(fileId);
    }

    /// <summary>从回收站还原（回到原分组）</summary>
    public static void RestoreFromRecycle(string fileId) => RecycleIds.Remove(fileId);

    /// <summary>清空回收站：里面的文件彻底消失</summary>
    public static void EmptyRecycle()
    {
        foreach (var id in RecycleIds)
            PurgedIds.Add(id);
        RecycleIds.Clear();
    }
}
