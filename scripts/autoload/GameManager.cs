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
    //
    // 【第十一轮：为什么起点从 0 改成 50】
    // 原来好感度/勇气值从 0 开始往上加，结果两个问题：
    //   1) 选"减勇气 -3"这种选项时，数值早就在 0 了，扣了等于没扣，玩家感觉不到代价；
    //   2) 三个选项里总有一个"哪头都不亏"的标准答案（试玩反馈 A4）。
    // 现在从 50（一个普通人的中点）出发，每个选择都有得有失，档位判定（50/70）也立刻有意义。
    public const int StatStart = 50;

    public int Affection { get; private set; } = StatStart;   // 好感度（0-100）
    public int Courage { get; private set; } = StatStart;     // 勇气值（0-100）

    // 当前章节编号（0=序章，1-15=各章）
    public int CurrentChapter { get; set; } = 0;

    // 隐藏物品收集（清单在 data/hidden_items.json，总数由清单决定）
    public int HiddenItemsFound { get; private set; } = 0;

    /// <summary>本 DEMO 隐藏物品总数（读自 data/hidden_items.json）</summary>
    public static int TotalHiddenItems => DataStore.HiddenItemTotal;

    // 已经拿到手的隐藏物品 id（防止同一个东西被重复点数）
    private readonly System.Collections.Generic.HashSet<string> hiddenItemIds = new();

    // 玩家做过的选择记录（用于回看和结局判定）
    private System.Collections.Generic.Dictionary<string, int> choiceHistory = new();

    // 已经触发过的"手机活起来"动态事件 id（第十二轮；存档保留，重玩同一章不会重复收到）
    private readonly System.Collections.Generic.List<string> liveEvents = new();

    // 已经"进去看过"的系统页面（第十五轮：订阅号/微信运动这类写死角标，看过就消，存档保留）
    private readonly System.Collections.Generic.HashSet<string> seenPages = new();

    // 背包里的物品 id（第十五轮：手机 + 剧情物品 + 隐藏物品；存档保留）
    private readonly System.Collections.Generic.HashSet<string> inventory = new();

    // 背包里"已经看过"的物品 id（第十五轮：背包按钮上的小红点用；打开一次背包就全算看过）
    private readonly System.Collections.Generic.HashSet<string> bagSeen = new();

    /// <summary>做过的选择条数（调试面板显示用）</summary>
    public int ChoiceCount => choiceHistory.Count;

    /// <summary>只读的选择记录（结束页"选择回顾"用）</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, int> ChoiceHistory => choiceHistory;

    // 存档格式版本：2 = 好感/勇气从 50 起点的版本
    private const int SaveVersion = 2;

    // 存档文件路径
    private const string SavePath = "user://savegame.json";

    // 开始新游戏前，旧存档会被备份到这里（试玩反馈 A1：不能一键抹掉进度）
    private const string PrevSavePath = "user://savegame_prev.json";

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
    /// 找到一个隐藏物品（按 id 记账：同一个东西重复点不会重复计数）
    /// </summary>
    /// <param name="itemId">物品 id，对应 data/hidden_items.json 里的 items[].id</param>
    public void FoundHiddenItem(string itemId = "")
    {
        if (!string.IsNullOrEmpty(itemId) && !hiddenItemIds.Add(itemId))
        {
            GD.Print($"[隐藏物品] {itemId} 已经拿过了，不重复计数");
            return;
        }

        HiddenItemsFound = Mathf.Min(HiddenItemsFound + 1, TotalHiddenItems);
        // 第十五轮：隐藏物品同时进背包展示（拾取弹窗、物品详情都在背包里看）
        if (!string.IsNullOrEmpty(itemId))
            AddItem(itemId);
        GD.Print($"[隐藏物品] {HiddenItemsFound}/{TotalHiddenItems}：{DataStore.HiddenItemName(itemId)}");
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

    // ========== 手机动态事件（第十二轮） ==========

    /// <summary>这个动态事件是不是已经触发过？（重玩章节不重复收到）</summary>
    public bool HasLiveEvent(string eventId) => !string.IsNullOrEmpty(eventId) && liveEvents.Contains(eventId);

    /// <summary>已触发过的动态事件 id 清单（只读；手机打开时用来"补投递"）</summary>
    public System.Collections.Generic.IReadOnlyList<string> TriggeredLiveEvents => liveEvents;

    /// <summary>记下一条已经送达的动态事件（会进存档）</summary>
    public void RecordLiveEvent(string eventId)
    {
        if (!string.IsNullOrEmpty(eventId) && !liveEvents.Contains(eventId))
        {
            liveEvents.Add(eventId);
            SaveGame(); // 送达就落档，强退也不丢红点状态
            GD.Print($"[动态事件] 已记录：{eventId}");
        }
    }

    // ========== 系统页面"看过"标记（第十五轮） ==========

    /// <summary>这个系统页面（订阅号/微信运动…）是不是已经进去看过？（写死角标消掉用）</summary>
    public bool HasSeenPage(string pageId) => !string.IsNullOrEmpty(pageId) && seenPages.Contains(pageId);

    /// <summary>记下"这个系统页面看过了"（会进存档）</summary>
    public void MarkPageSeen(string pageId)
    {
        if (!string.IsNullOrEmpty(pageId) && seenPages.Add(pageId))
        {
            SaveGame(); // 看过就落档，写死角标不会因强退复活
            GD.Print($"[系统页面] 已看过：{pageId}");
        }
    }

    // ========== 背包（第十五轮） ==========

    /// <summary>背包里有没有这件物品</summary>
    public bool HasItem(string itemId) => !string.IsNullOrEmpty(itemId) && inventory.Contains(itemId);

    /// <summary>往背包里放一件物品（重复放无效；会进存档）</summary>
    public void AddItem(string itemId)
    {
        if (!string.IsNullOrEmpty(itemId) && inventory.Add(itemId))
        {
            SaveGame(); // 拿到东西就落档，别等切场景
            GD.Print($"[背包] 获得物品：{itemId}");
        }
    }

    /// <summary>背包里的全部物品 id（只读）</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> InventoryItems => inventory;

    /// <summary>背包里有没有"还没看过"的新东西（背包按钮上的小红点用）</summary>
    public bool HasNewBagItems
    {
        get
        {
            foreach (var id in inventory)
                if (!bagSeen.Contains(id))
                    return true;
            return false;
        }
    }

    /// <summary>这件东西在背包里被看过没有（背包格子上的"新"标记用）</summary>
    public bool HasBagSeen(string itemId) => bagSeen.Contains(itemId);

    /// <summary>玩家打开过背包 → 现有物品都算"看过"了（会进存档）</summary>
    public void MarkBagSeen()
    {
        bool changed = false;
        foreach (var id in inventory)
            changed |= bagSeen.Add(id);
        if (changed)
            SaveGame();
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
            version = SaveVersion,
            affection = Affection,
            courage = Courage,
            currentChapter = CurrentChapter,
            hiddenItemsFound = HiddenItemsFound,
            hiddenItemIds = new System.Collections.Generic.List<string>(hiddenItemIds),
            choiceHistory = choiceHistory,
            liveEvents = new System.Collections.Generic.List<string>(liveEvents),
            seenPages = new System.Collections.Generic.List<string>(seenPages),
            inventory = new System.Collections.Generic.List<string>(inventory),
            bagSeen = new System.Collections.Generic.List<string>(bagSeen)
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
            if (saveData == null)
            {
                GD.PrintErr("[读档] 存档内容是空的，按无存档处理");
                return false;
            }

            // 把读到的数据恢复到当前状态
            Affection = saveData.affection;
            Courage = saveData.courage;
            CurrentChapter = saveData.currentChapter;
            HiddenItemsFound = saveData.hiddenItemsFound;
            choiceHistory = saveData.choiceHistory ?? new();
            hiddenItemIds.Clear();
            foreach (var id in saveData.hiddenItemIds ?? new())
                hiddenItemIds.Add(id);

            // 动态事件清单（老存档没有这个字段 → 读出 null，按空处理即可，不用升版本）
            liveEvents.Clear();
            foreach (var id in saveData.liveEvents ?? new())
                if (!string.IsNullOrEmpty(id))
                    liveEvents.Add(id);

            // 系统页面"看过"标记 + 背包（第十五轮；老存档同样按空处理）
            seenPages.Clear();
            foreach (var id in saveData.seenPages ?? new())
                if (!string.IsNullOrEmpty(id))
                    seenPages.Add(id);
            inventory.Clear();
            foreach (var id in saveData.inventory ?? new())
                if (!string.IsNullOrEmpty(id))
                    inventory.Add(id);
            // 隐藏物品老档里记在 hiddenItemIds，背包一并收编，别让老玩家"拿到了但背包里没有"
            foreach (var id in hiddenItemIds)
                inventory.Add(id);

            // 背包"看过"标记（第十五轮；老存档没有这个字段 → 按空处理）
            bagSeen.Clear();
            foreach (var id in saveData.bagSeen ?? new())
                if (!string.IsNullOrEmpty(id))
                    bagSeen.Add(id);

            // 手机永远在身上：老存档里没有"手机"这件物品，读档时补进去，
            // 玩家会看到背包红点亮起 —— 正好借小红点告诉他"现在有个背包了"
            inventory.Add("phone");

            // 旧存档（第一版：好感/勇气从 0 起步）自动迁移到"50 起点"的新口径，
            // 否则老玩家的数值会莫名其妙偏低一档。
            if (saveData.version < SaveVersion)
            {
                Affection = Mathf.Clamp(Affection + StatStart, 0, 100);
                Courage = Mathf.Clamp(Courage + StatStart, 0, 100);
                GD.Print($"[读档] 检测到 v{saveData.version} 旧存档，数值已平移到 {StatStart} 起点口径");
                SaveGame(); // 顺手把升级后的存档写回去
            }

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
    /// 开始新游戏：先把旧存档备份成 savegame_prev.json，再清零。
    /// （试玩反馈 A1：原来点一下"开始游戏"，进度就当场没了，连个招呼都不打）
    /// </summary>
    public void BeginNewGame()
    {
        if (HasSaveFile())
        {
            BackupSave();
        }

        ResetGame();
        SaveGame();
    }

    /// <summary>把当前存档原样复制一份到 savegame_prev.json（复制失败不影响开始新游戏）</summary>
    private void BackupSave()
    {
        using var src = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (src == null)
        {
            GD.PrintErr("[存档] 备份失败：读不到旧存档");
            return;
        }

        string json = src.GetAsText();

        using var dst = FileAccess.Open(PrevSavePath, FileAccess.ModeFlags.Write);
        if (dst == null)
        {
            GD.PrintErr($"[存档] 备份失败：{FileAccess.GetOpenError()}");
            return;
        }

        dst.StoreString(json);
        GD.Print("[存档] 旧存档已备份到 savegame_prev.json");
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
        Affection = StatStart;
        Courage = StatStart;
        CurrentChapter = 0;
        HiddenItemsFound = 0;
        hiddenItemIds.Clear();
        choiceHistory.Clear();
        liveEvents.Clear();
        seenPages.Clear();
        inventory.Clear();
        inventory.Add("phone"); // 手机永远在身上（第十五轮：随时能翻出来看）
        bagSeen.Clear();        // 全新的背包 → 红点亮着，提示玩家"翻开看看"
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
    // 存档结构版本号：老存档缺这个字段时读出来是 0，正好用来触发迁移
    public int version { get; set; }
    public int affection { get; set; }
    public int courage { get; set; }
    public int currentChapter { get; set; }
    public int hiddenItemsFound { get; set; }
    public System.Collections.Generic.List<string> hiddenItemIds { get; set; }
    public System.Collections.Generic.Dictionary<string, int> choiceHistory { get; set; }

    // 已送达的手机动态事件 id 清单（第十二轮；老存档里没有这个字段 → null → 按空处理）
    public System.Collections.Generic.List<string> liveEvents { get; set; }

    // 已"进去看过"的系统页面 id（第十五轮；老存档里没有这个字段）
    public System.Collections.Generic.List<string> seenPages { get; set; }

    // 背包物品 id 清单（第十五轮；老存档里没有这个字段）
    public System.Collections.Generic.List<string> inventory { get; set; }

    // 背包里"已经看过"的物品 id（第十五轮；老存档里没有这个字段 → 全算没看过，红点点亮）
    public System.Collections.Generic.List<string> bagSeen { get; set; }
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
