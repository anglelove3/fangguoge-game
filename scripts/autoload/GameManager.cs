using Godot;
using System;
using System.Text.Json;

/// <summary>
/// 游戏全局管理器（自动加载单例）
/// 负责：场景切换、游戏状态、存档读档、章节进度
///
/// 【Day 2 知识点 - C# 单例模式】
/// 这个类在 project.godot 中注册为"自动加载"，
/// 意味着游戏一启动它就存在，而且全局只有一个实例。
/// 任何场景都可以通过 GameManager.Instance 访问它。
/// </summary>
public partial class GameManager : Node
{
    // ========== 单例访问 ==========
    // 静态变量 Instance 指向唯一实例，任何地方都能访问
    public static GameManager Instance { get; private set; }

    // ========== 游戏属性 ==========
    public int Affection { get; private set; } = 0;   // 好感度（0-100）
    public int Courage { get; private set; } = 0;     // 勇气值（0-100）

    // 当前章节编号（0=序章，1-15=各章）
    public int CurrentChapter { get; set; } = 0;

    // 隐藏物品收集
    public int HiddenItemsFound { get; private set; } = 0;
    public const int TotalHiddenItems = 10;

    // 玩家做过的选择记录（用于回看和结局判定）
    private System.Collections.Generic.Dictionary<string, int> choiceHistory = new();

    // 存档文件路径
    private const string SavePath = "user://savegame.json";

    // ========== Godot 生命周期 ==========

    /// <summary>
    /// _Ready() 相当于"构造函数"——节点第一次进入场景树时调用一次
    /// 【Day 2 知识点 - Godot 生命周期】
    /// _Ready → _Process(每帧) → _ExitTree(离开时)
    /// </summary>
    public override void _Ready()
    {
        // 设置单例引用
        Instance = this;

        GD.Print("===== 放过哥游戏启动 =====");
        GD.Print("GameManager 已就绪");
    }

    // ========== 场景切换 ==========

    /// <summary>
    /// 切换到指定场景，同时更新章节编号（直接切换，无过渡）
    /// </summary>
    public void ChangeScene(string scenePath, int chapterIndex = -1)
    {
        if (chapterIndex >= 0)
        {
            CurrentChapter = chapterIndex;
        }

        GD.Print($"[场景切换] 章节 {CurrentChapter} → {scenePath}");
        GetTree().ChangeSceneToFile(scenePath);
    }

    /// <summary>
    /// 带淡入淡出过渡效果的场景切换
    /// </summary>
    public void ChangeSceneWithTransition(string scenePath, int chapterIndex = -1)
    {
        if (chapterIndex >= 0)
        {
            CurrentChapter = chapterIndex;
        }

        GD.Print($"[场景切换(带过渡)] 章节 {CurrentChapter} → {scenePath}");
        TransitionManager.FadeToScene(scenePath);
    }

    /// <summary>
    /// 返回主菜单（重置章节编号）
    /// </summary>
    public void ReturnToMainMenu()
    {
        GD.Print("[场景切换] → 主菜单");
        CurrentChapter = 0;
        GetTree().ChangeSceneToFile("res://scenes/main_menu/main_menu.tscn");
    }

    // ========== 属性修改 ==========

    /// <summary>
    /// 增加好感度（传负数则减少），自动限制在 0-100 范围
    /// </summary>
    public void AddAffection(int value)
    {
        Affection = Mathf.Clamp(Affection + value, 0, 100);
        GD.Print($"[好感度] {Affection} ({(value >= 0 ? "+" : "")}{value})");
    }

    /// <summary>
    /// 增加勇气值（传负数则减少），自动限制在 0-100 范围
    /// </summary>
    public void AddCourage(int value)
    {
        Courage = Mathf.Clamp(Courage + value, 0, 100);
        GD.Print($"[勇气值] {Courage} ({(value >= 0 ? "+" : "")}{value})");
    }

    /// <summary>
    /// 找到一个隐藏物品
    /// </summary>
    public void FoundHiddenItem()
    {
        HiddenItemsFound = Mathf.Min(HiddenItemsFound + 1, TotalHiddenItems);
        GD.Print($"[隐藏物品] {HiddenItemsFound}/{TotalHiddenItems}");
    }

    /// <summary>
    /// 记录玩家的选择（用于结局判定和回看）
    /// </summary>
    /// <param name="choiceId">选择的唯一标识，如 "ch01_order_food"</param>
    /// <param name="optionIndex">选择了第几个选项（0, 1, 2...）</param>
    public void RecordChoice(string choiceId, int optionIndex)
    {
        choiceHistory[choiceId] = optionIndex;
        GD.Print($"[选择记录] {choiceId} = 选项{optionIndex}");
    }

    // ========== 存档系统 ==========

    /// <summary>
    /// 保存游戏进度到文件
    /// 【Day 2 知识点 - C# JSON 序列化】
    /// 把游戏状态转成 JSON 文本，写到文件中
    /// </summary>
    public void SaveGame()
    {
        // 创建一个存档数据对象
        var saveData = new SaveData
        {
            affection = Affection,
            courage = Courage,
            currentChapter = CurrentChapter,
            hiddenItemsFound = HiddenItemsFound,
            choiceHistory = choiceHistory
        };

        // 设置 JSON 格式化为可读格式（方便调试）
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        // 把对象转成 JSON 字符串
        string json = JsonSerializer.Serialize(saveData, options);

        // 用 Godot 的文件 API 写入（user:// 指向用户数据目录）
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(json);
            GD.Print($"[存档] 游戏已保存到 {SavePath}");
        }
        else
        {
            GD.PrintErr("[存档] 保存失败！无法打开文件");
        }
    }

    /// <summary>
    /// 从文件读取游戏进度
    /// </summary>
    /// <returns>是否成功读取</returns>
    public bool LoadGame()
    {
        // 检查存档文件是否存在
        if (!FileAccess.FileExists(SavePath))
        {
            GD.Print("[读档] 没有找到存档");
            return false;
        }

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr("[读档] 打开存档失败");
            return false;
        }

        string json = file.GetAsText();

        try
        {
            // 把 JSON 字符串还原成 SaveData 对象
            var saveData = JsonSerializer.Deserialize<SaveData>(json);

            // 把读到的数据恢复到当前状态
            Affection = saveData.affection;
            Courage = saveData.courage;
            CurrentChapter = saveData.currentChapter;
            HiddenItemsFound = saveData.hiddenItemsFound;
            choiceHistory = saveData.choiceHistory ?? new();

            GD.Print("[读档] 存档加载成功！");
            GD.Print($"  章节: {CurrentChapter}, 好感度: {Affection}, 勇气值: {Courage}");
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[读档] JSON 解析失败: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 是否有存档
    /// </summary>
    public bool HasSaveFile()
    {
        return FileAccess.FileExists(SavePath);
    }

    /// <summary>
    /// 删除存档（重置游戏用）
    /// </summary>
    public void DeleteSave()
    {
        if (FileAccess.FileExists(SavePath))
        {
            using var dir = DirAccess.Open("user://");
            dir?.Remove("savegame.json");
            GD.Print("[存档] 已删除");
        }
    }

    // ========== 结局判定 ==========

    /// <summary>
    /// 根据属性值判定结局类型
    /// </summary>
    public EndingType GetEndingType()
    {
        // 隐藏结局：好感度低但找齐所有隐藏物品
        if (HiddenItemsFound >= TotalHiddenItems && Affection < 50)
            return EndingType.NowIUnderstand;

        // 完美结局：好感度和勇气值都很高
        if (Affection >= 70 && Courage >= 70)
            return EndingType.SunnyDay;

        // 温暖结局：中等水平
        if (Affection >= 50 && Courage >= 50)
            return EndingType.SouthWind;

        // 遗憾结局：勇气值不够
        return EndingType.HermitCrab;
    }

    /// <summary>
    /// 重置所有游戏状态（新游戏用）
    /// </summary>
    public void ResetGame()
    {
        Affection = 0;
        Courage = 0;
        CurrentChapter = 0;
        HiddenItemsFound = 0;
        choiceHistory.Clear();
        GD.Print("[重置] 游戏状态已重置");
    }
}

// ========== 存档数据结构 ==========

/// <summary>
/// 存档数据的结构定义
/// 【Day 2 知识点 - C# 类】
/// 这个类专门用来描述"存档长什么样"，
/// 字段名对应 JSON 里的键名。
/// </summary>
public class SaveData
{
    public int affection { get; set; }
    public int courage { get; set; }
    public int currentChapter { get; set; }
    public int hiddenItemsFound { get; set; }
    public System.Collections.Generic.Dictionary<string, int> choiceHistory { get; set; }
}

/// <summary>
/// 结局类型枚举
/// 【Day 2 知识点 - C# 枚举 enum】
/// enum 把一组有意义的名字和数字对应起来，代码里用名字更直观
/// </summary>
public enum EndingType
{
    SunnyDay = 1,        // 晴天（完美结局）
    SouthWind = 2,       // 南风（温暖结局）
    HermitCrab = 3,      // 寄居蟹（遗憾结局）
    NowIUnderstand = 4   // 开始懂了（隐藏结局）
}
