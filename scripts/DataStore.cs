using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>是不是隐藏物品（背包里给它加一枚小标签）</summary>
    public static bool IsHiddenItem(string id) =>
        Hidden.Items?.Exists(i => i.Id == id) ?? false;

    // ========== 背包图鉴（第十五轮） ==========

    private static InventoryFile inventoryData;

    private static InventoryFile Inventory =>
        inventoryData ??= LoadJson<InventoryFile>("res://data/inventory.json") ?? new InventoryFile();

    /// <summary>背包图鉴全表（背包界面按这个顺序摆格子）</summary>
    public static List<InventoryItemEntry> InventoryRegistry => Inventory.Items;

    /// <summary>背包物品的图鉴定义（名字键、描述键、图标、动作）</summary>
    public static InventoryItemEntry ItemDef(string id) =>
        Inventory.Items?.Find(i => i.Id == id);

    /// <summary>某件东西在背包里的显示名（隐藏物品没写名字键，退回 hidden_items.json 里的名字）</summary>
    public static string ItemName(string id)
    {
        var def = ItemDef(id);
        if (def != null && !string.IsNullOrEmpty(def.NameKey))
            return Text(def.NameKey);
        return HiddenItemName(id);
    }

    // ========== 结束页"选择回顾" ==========

    private static ReviewFile review;

    public static List<ReviewItem> ReviewItems =>
        (review ??= LoadJson<ReviewFile>("res://data/text/demo_review.json"))?.Items ?? new();

    // ========== 人物设定（角色卡，data/characters.json） ==========
    //
    // 第十七轮起，"这个人是谁"只有一份真相：data/characters.json。
    // 昵称颜色、头像、关系阶段默认值全部从这里读，C# 里不再出现任何人名分支。
    // 想给江洁换姓、改颜色、加别名，改那个 JSON 就行，不用碰代码。

    private static CharactersFile charactersData;

    private static CharactersFile Characters =>
        charactersData ??= LoadJson<CharactersFile>("res://data/characters.json") ?? new CharactersFile();

    /// <summary>全表人物（顺序 = 角色卡顺序，主角在前）</summary>
    public static List<CharacterEntry> Cast => Characters.Cast ?? new();

    /// <summary>按 id 取人物（代码里写 "jie" 取到的就是江洁）</summary>
    public static CharacterEntry Character(string id) =>
        Cast.Find(c => c.Id == id);

    /// <summary>
    /// 按台词里的称呼取人物。称呼可能写成"俊杰"，而角色卡里叫"俊杰（师弟）"，
    /// 所以先剥掉全角括号里的注释，再双向 Contains 比对；别名（aka）同样参与匹配。
    /// </summary>
    public static CharacterEntry CharacterBySpeaker(string speaker)
    {
        if (string.IsNullOrEmpty(speaker))
            return null;
        foreach (var c in Cast)
        {
            if (Matches(c.Name, speaker) || c.Aka?.Any(a => Matches(a, speaker)) == true)
                return c;
        }
        return null;
    }

    private static bool Matches(string cardName, string speaker)
    {
        if (string.IsNullOrEmpty(cardName))
            return false;
        var baseName = cardName.Split('（')[0].Trim();
        if (baseName.Length == 0)
            return false;
        return baseName == speaker || speaker.Contains(baseName) || baseName.Contains(speaker);
    }

    /// <summary>某句台词说话人的昵称颜色（角色卡没这个人时用兜底色，不报错）</summary>
    public static Color SpeakerColor(string speaker, Color fallback)
    {
        var c = CharacterBySpeaker(speaker);
        if (c == null)
            return fallback;
        // "#RRGGBB" 才交给 FromHtml 解析——它不校验格式，写成别的只会静默变黑，所以先自己查。
        var hex = c.Color;
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#") || hex.Length != 7)
            return fallback;
        return Color.FromHtml(hex);
    }

    /// <summary>某个人物的微信头像路径（没有或空串返回 ""）</summary>
    public static string CharacterAvatar(string id) => Character(id)?.Avatar ?? "";
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

/// <summary>背包物品图鉴文件（对应 data/inventory.json）</summary>
public class InventoryFile
{
    public List<InventoryItemEntry> Items { get; set; } = new();
}

/// <summary>图鉴里的一件物品：id + 文案键 + 图标 + 详情页动作</summary>
public class InventoryItemEntry
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = ""; // ui_text 里的名字键（隐藏物品留空 → 用 hidden_items 的名字）
    public string DescKey { get; set; } = ""; // 描述键（可留空）
    public string Icon { get; set; } = "";    // 图标路径
    public string Action { get; set; } = "";  // 详情页动作：目前只有 "phone"（打开手机）
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

/// <summary>角色卡文件（对应 data/characters.json）</summary>
public class CharactersFile
{
    public List<CharacterEntry> Cast { get; set; } = new();
}

/// <summary>一个人物的设定：他是谁、长什么样口径、怎么说话、昵称什么颜色</summary>
public class CharacterEntry
{
    public string Id { get; set; } = "";              // 程序内代号（jie / jiegen…），不进玩家视野
    public string Name { get; set; } = "";            // 显示名
    public List<string> Aka { get; set; } = new();    // 别名 / 外号，也参与昵称着色匹配
    public string Line { get; set; } = "";            // 所属圈子（感情线 / 实验室 / 老家…）
    public string Debut { get; set; } = "";           // 首次出场章节
    public string Identity { get; set; } = "";        // 身份设定
    public string Look { get; set; } = "";            // 形象口径（画立绘、挑头像时照这个走）
    public string Voice { get; set; } = "";           // 说话方式（写台词时的准绳）
    public string Color { get; set; } = "";           // 昵称颜色 "#RRGGBB"
    public string Avatar { get; set; } = "";          // 微信头像路径
    public List<string> Portraits { get; set; } = new(); // 立绘名
    public string Relationship { get; set; } = "";    // 与主角的关系（写给人看的说明）
    public string Stage { get; set; } = "";           // 关系阶段出厂值（just_met / ex…），存档里的值覆盖它
    public List<string> Editable { get; set; } = new();   // 哪些地方还留着改动空间
    public List<string> EditFiles { get; set; } = new();  // 改这个人要一起动的文件
}
