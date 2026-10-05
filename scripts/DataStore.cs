using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 数据仓库（静态工具类）—— 把所有"给玩家看的文字"和"关卡里的布局数字"集中读取
///
/// 【为什么要这个类】
/// 第十轮试玩反馈里有一条：提示语、卡片标题、结局名这些文案硬编码在 C# 里，
/// 想改一个字就得改代码。现在它们全部搬到 data/ 下的 JSON 文件里：
///   data/text/ui_text.json      —— 界面文案（按钮、提示、飘字、设置页……）
///   data/text/demo_review.json  —— 结束页"选择回顾"要列出的节点
///   data/chapters/ch03.json     —— 第三章卡片/补偿物的文字键与坐标
///   data/hidden_items.json      —— 隐藏物品清单（总数由它决定，不再是写死的 10）
///
/// 【知识点 - 懒加载 + 缓存】
/// 第一次用到某个文件时才去读盘解析，之后从字典里拿，
/// 这样游戏启动不会被一堆 JSON 拖慢。
/// </summary>
public static class DataStore
{
    // ========== 通用 JSON 读取 ==========

    /// <summary>读一个 JSON 文件并反序列化成 T；失败返回 null（并打印错误，不会让游戏崩）</summary>
    public static T LoadJson<T>(string path) where T : class
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[数据] 找不到文件：{path}");
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(FileAccess.GetFileAsString(path), options);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[数据] 解析失败 {path}：{e.Message}");
            return null;
        }
    }

    // ========== 界面文案 ==========

    private static Dictionary<string, string> textTable;

    private static void EnsureText()
    {
        // ui_text.json 就是"键: 文案"的一层平铺对象，直接读成字典最省事。
        textTable ??= LoadJson<Dictionary<string, string>>("res://data/text/ui_text.json") ?? new();
    }

    /// <summary>取一句文案。找不到时返回 key 本身——这样漏写哪一条一眼就能看出来。</summary>
    public static string Text(string key)
    {
        EnsureText();
        return textTable != null && textTable.TryGetValue(key, out var v) ? v : key;
    }

    /// <summary>带占位符的文案：Text("hint.ch03_cards", 1, 3) → "…（1/3）"</summary>
    public static string Text(string key, params object[] args)
    {
        return string.Format(Text(key), args);
    }

    // ========== 章节布局数据 ==========

    private static readonly Dictionary<string, ChapterLayout> layouts = new();

    /// <summary>读 data/chapters/{id}.json（卡片坐标、天平倾角这类"可调的数字"）</summary>
    public static ChapterLayout GetChapter(string id)
    {
        if (!layouts.TryGetValue(id, out var data))
        {
            data = LoadJson<ChapterLayout>($"res://data/chapters/{id}.json") ?? new ChapterLayout();
            layouts[id] = data;
        }
        return data;
    }

    // ========== 隐藏物品 ==========

    private static HiddenItemsFile hiddenItems;

    private static HiddenItemsFile Hidden =>
        hiddenItems ??= LoadJson<HiddenItemsFile>("res://data/hidden_items.json") ?? new HiddenItemsFile();

    /// <summary>本 DEMO 里一共能收集多少个隐藏物品（= 清单条数，不再是写死的 10）</summary>
    public static int HiddenItemTotal => Hidden.Items?.Count ?? 0;

    /// <summary>某个隐藏物品的名字（给飘字/回顾用）</summary>
    public static string HiddenItemName(string id)
    {
        var item = Hidden.Items?.Find(i => i.Id == id);
        return item?.Name ?? id;
    }

    // ========== 结束页"选择回顾" ==========

    private static ReviewFile review;

    public static List<ReviewItem> ReviewItems =>
        (review ??= LoadJson<ReviewFile>("res://data/text/demo_review.json"))?.Items ?? new();
}

// ==================== 数据结构 ====================

/// <summary>章节可调数据（目前只有第三章用到卡片/补偿物两块）</summary>
public class ChapterLayout
{
    public List<CardEntry> Cards { get; set; } = new();
    public List<ChipEntry> Compensations { get; set; } = new();
    public SizeEntry CardSize { get; set; } = new();
    public SizeEntry ChipSize { get; set; } = new();
    public List<float> TiltAfterCards { get; set; } = new();
    public List<float> TiltAfterChips { get; set; } = new();
}

/// <summary>一张记忆卡片：id + 配图 + 文案键 + 坐标 + 飞入时的纸片角</summary>
public class CardEntry
{
    public string Id { get; set; } = "";
    public string Image { get; set; } = "";
    public string TitleKey { get; set; } = "";
    public string SubKey { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Tilt { get; set; }
}

/// <summary>一个"补偿物"（往左盘加的东西：道歉 / 成绩 / 熬夜）</summary>
public class ChipEntry
{
    public string Id { get; set; } = "";
    public string TitleKey { get; set; } = "";
    public string SubKey { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>尺寸（JSON 里写成 { "w": 360, "h": 176 }）</summary>
public class SizeEntry
{
    public float W { get; set; } = 340;
    public float H { get; set; } = 168;
}

/// <summary>隐藏物品清单文件</summary>
public class HiddenItemsFile
{
    public List<HiddenItemEntry> Items { get; set; } = new();
}

public class HiddenItemEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Chapter { get; set; }
}

/// <summary>结束页"选择回顾"要列出的节点（对应 data/text/demo_review.json）</summary>
public class ReviewFile
{
    public List<ReviewItem> Items { get; set; } = new();
}

public class ReviewItem
{
    public string Choice { get; set; } = "";  // 对应 GameManager 记录的选择 id（如 ch01_order_food）
    public string Chapter { get; set; } = ""; // 这一处选择属于哪一章（如 ch01）
    public string Where { get; set; } = "";   // 回顾里那一行的小标题（如 第一章《寄居蟹》）
    public string Label { get; set; } = "";   // 回顾里显示的提问

    /// <summary>choices 里的组名：choice 去掉"章节名_"前缀就是它</summary>
    public string GroupId => Choice != null && Choice.StartsWith(Chapter + "_")
        ? Choice.Substring(Chapter.Length + 1)
        : Choice;
}
