using Godot;
using System;
using System.Linq;
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

    // 人物关系账（第十七轮：谁加了好友、备注叫什么、关系到哪一步、关键节点发生在哪一章）。
    // 键 = data/characters.json 里的角色 id（jie / jiegen…），不是显示名——所以改名字不会弄丢进度。
    private readonly System.Collections.Generic.Dictionary<string, RelationshipState> relationships = new();

    /// <summary>做过的选择条数（调试面板显示用）</summary>
    public int ChoiceCount => choiceHistory.Count;

    /// <summary>只读的选择记录（结束页"选择回顾"用）</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, int> ChoiceHistory => choiceHistory;

    // 存档格式版本：3 = 在 v2 的基础上加了"人物关系"一层（加好友 / 备注 / 关系阶段 / 关键节点）
    private const int SaveVersion = 3;

    // 数值口径迁移只发生在"从 v1（0 起点）升到 v2（50 起点）"这一步。
    // 单独留一个常量，是因为升到 v3 时如果还写 version < SaveVersion，
    // 老玩家的好感/勇气会被再加一次 50 —— 这个坑必须跟版本升级解绑。
    private const int StatShiftVersion = 2;

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
        // 一按就落档：第八章有六幕，玩家在第二幕选完就强退的话，
        // 结束页的「选择回顾」不该把这一步当成没发生过。
        // （顺手把同一刻的数值/关系一起带走了——存档是整份写的。）
        SaveGame();
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

    // ========== 人物关系（第十七轮） ==========
    //
    // 角色卡（data/characters.json）管"这个人出厂时是谁"，
    // 这里管"玩家和他走到了哪一步"：加没加好友、备注叫什么、关系到哪一档、
    // 身上发生过哪些关键节点。四样都进存档，重开游戏不会忘记。
    //
    // 【为什么用角色 id 而不是名字当键】
    // 名字是给人看的，随时能改（江洁换姓只改文本）；id 是程序内的代号。
    // 拿 id 当键，改名字就不会把玩家的进度弄丢。

    /// <summary>全部人物关系账（只读；调试面板和未来章节用）</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, RelationshipState> Relationships => relationships;

    /// <summary>
    /// 取一个人的关系账。没有记录时当场建一条空的，关系阶段先用角色卡里的出厂值（stage 字段）。
    /// 注意这里不写存档——只有玩家真的改变了什么才落盘。
    /// </summary>
    public RelationshipState Relationship(string characterId)
    {
        if (string.IsNullOrEmpty(characterId))
            return new RelationshipState();

        if (!relationships.TryGetValue(characterId, out var state))
        {
            state = new RelationshipState
            {
                stage = DataStore.Character(characterId)?.Stage ?? "",
            };
            relationships[characterId] = state;
        }
        return state;
    }

    /// <summary>加过好友没有（没有记录就是没加）</summary>
    public bool IsFriended(string characterId)
    {
        return !string.IsNullOrEmpty(characterId)
            && relationships.TryGetValue(characterId, out var state)
            && state.friended;
    }

    /// <summary>
    /// 加好友。返回 true 表示"这一次是新加的"——重复加不会重复落档，
    /// 但带进来的关键节点照样记账（剧情里"她推了名片"这类事只演一次）。
    /// </summary>
    public bool AddFriend(string characterId, string eventId = "")
    {
        var state = Relationship(characterId);
        bool isNew = !state.friended;
        state.friended = true;
        AddEvent(state, eventId);
        if (isNew)
        {
            SaveGame();
            GD.Print($"[关系] 加好友：{characterId}");
        }
        return isNew;
    }

    /// <summary>玩家给这个人改的备注名；没改过返回空串（调用方自己回落到联系人本名）</summary>
    public string RemarkOf(string characterId)
    {
        return !string.IsNullOrEmpty(characterId) && relationships.TryGetValue(characterId, out var state)
            ? state.remark
            : "";
    }

    /// <summary>改备注：当场落盘，微信的会话列表 / 通讯录 / 聊天页顶栏立刻跟着变</summary>
    public void SetRemark(string characterId, string remark, string eventId = "")
    {
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(remark))
            return;

        var state = Relationship(characterId);
        bool remarkChanged = state.remark != remark;
        bool eventNew = AddEvent(state, eventId);
        if (!remarkChanged && !eventNew)
            return; // 备注没变、节点也记过了 → 不重复写盘

        state.remark = remark;
        SaveGame();
        GD.Print($"[关系] 备注：{characterId} → {remark}");
    }

    /// <summary>关系阶段（just_met / friend / dating…）；玩家没推进过时读角色卡的出厂值</summary>
    public string StageOf(string characterId) => Relationship(characterId).stage;

    /// <summary>推进关系阶段（结局判定、后续章节的"我们算什么"会用到）</summary>
    public void SetStage(string characterId, string stage)
    {
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(stage))
            return;

        var state = Relationship(characterId);
        if (state.stage == stage)
            return;

        state.stage = stage;
        SaveGame();
        GD.Print($"[关系] 阶段：{characterId} → {stage}");
    }

    /// <summary>这个人身上是不是已经记过某个关键节点（第八章之后所有章节都靠它去重）</summary>
    public bool HasRelationshipEvent(string characterId, string eventId)
    {
        return !string.IsNullOrEmpty(characterId) && !string.IsNullOrEmpty(eventId)
            && relationships.TryGetValue(characterId, out var state)
            && state.keyEvents.Contains(eventId);
    }

    /// <summary>记一个关键节点（会落盘）</summary>
    public void RecordRelationshipEvent(string characterId, string eventId)
    {
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(eventId))
            return;
        var state = Relationship(characterId);
        if (AddEvent(state, eventId))
            SaveGame();
    }

    /// <summary>只在内存里加节点，返回"是不是新加的"（上面的公开方法各自决定什么时候写盘）</summary>
    private static bool AddEvent(RelationshipState state, string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return false;
        return state.keyEvents.Add(eventId);
    }

    /// <summary>
    /// 给自动化测试用的一行快照（GDScript 里 `gm.call("DebugSnapshot")` 就能拿到）。
    /// 【为什么要这个方法】GDScript 直接读 C# 属性时名号不一定对得上，
    /// 回归脚本每次都得猜；猜错就是"断言恒为 false 的假绿"。
    /// 一个方法把所有要检查的状态打包成 JSON，测试只解析字符串，不再猜属性名。
    /// </summary>
    public string DebugSnapshot()
    {
        int friended = 0, remarked = 0;
        foreach (var state in relationships.Values)
        {
            if (state.friended) friended++;
            if (!string.IsNullOrEmpty(state.remark)) remarked++;
        }

        var payload = new
        {
            affection = Affection,
            courage = Courage,
            chapter = CurrentChapter,
            choices = ChoiceCount,
            hiddenItems = HiddenItemsFound,
            friended,
            remarked,
            remarks = relationships
                .Where(pair => !string.IsNullOrEmpty(pair.Value.remark))
                .ToDictionary(pair => pair.Key, pair => pair.Value.remark),
            stages = relationships
                .Where(pair => !string.IsNullOrEmpty(pair.Value.stage))
                .ToDictionary(pair => pair.Key, pair => pair.Value.stage),
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// 把"早就认识的人"补进关系账（第十七轮）。
    /// 微信列表里本来就在的联系人，本来就是好友——只是以前没有这本账，没记下来。
    /// 补齐之后 IsFriended 对每个人都是诚实的答案，后面的章节可以直接问"他们加过好友了吗"，
    /// 不用在代码里记住"除了江洁之外都算认识"这种话。
    /// 群聊（kind=group）和系统账号（有 icon）不算好友；标了 requiresFriend 的也不补——那是等玩家自己加的。
    /// </summary>
    private void SeedRelationships()
    {
        foreach (var c in WeChatData.RawContacts.Contacts)
        {
            if (string.IsNullOrEmpty(c.Id) || c.RequiresFriend)
                continue;
            if (!string.IsNullOrEmpty(c.Kind) || !string.IsNullOrEmpty(c.Icon))
                continue;
            Relationship(c.Id).friended = true;
        }
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
            bagSeen = new System.Collections.Generic.List<string>(bagSeen),
            // 直接把这个字典写出去：RelationshipState 里的字段都是可序列化的简单类型
            relationships = relationships
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

            // 人物关系账（第十七轮；v2 及更早的存档没有这个字段 → 按空处理，
            // 之后谁被剧情改过备注、加过好友，才会重新出现在这本账上）
            relationships.Clear();
            foreach (var pair in saveData.relationships ?? new())
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                    continue;
                var state = pair.Value;
                if (string.IsNullOrEmpty(state.stage))
                    state.stage = DataStore.Character(pair.Key)?.Stage ?? "";
                if (state.keyEvents == null)
                    state.keyEvents = new System.Collections.Generic.HashSet<string>();
                relationships[pair.Key] = state;
            }
            SeedRelationships(); // 老档里没记下的"早就认识的人"补齐

            // 手机永远在身上：老存档里没有"手机"这件物品，读档时补进去，
            // 玩家会看到背包红点亮起 —— 正好借小红点告诉他"现在有个背包了"
            inventory.Add("phone");

            // 数值口径迁移：只有 v1（好感/勇气从 0 起步）需要平移到 50 起点口径。
            // 【第十七轮的坑，先记下】这里以前写的是 version < SaveVersion，
            // 版本从 2 升到 3 的那一刻，它会把 v2 老档的好感/勇气再加一次 50 —— 存档直接爆表。
            // 判据必须绑在"哪一版改的数值"上（StatShiftVersion = 2），不能绑当前版本号。
            bool shifted = false;
            if (saveData.version < StatShiftVersion)
            {
                Affection = Mathf.Clamp(Affection + StatStart, 0, 100);
                Courage = Mathf.Clamp(Courage + StatStart, 0, 100);
                GD.Print($"[读档] 检测到 v{saveData.version} 旧存档，数值已平移到 {StatStart} 起点口径");
                shifted = true;
            }

            // 存档落后于当前格式 → 顺手写回一次，把新字段（关系账）补上，
            // 下次读档就不用再走一遍迁移。
            if (shifted || saveData.version < SaveVersion)
                SaveGame();

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
        relationships.Clear();  // 全新的一局：备注清空、关系阶段回到角色卡出厂值
        SeedRelationships();    // 微信列表里本来就在的人，一开始就是好友
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

    // 人物关系账（第十七轮；键 = data/characters.json 的角色 id。
    // v2 及更早的存档没有这个字段 → 读出来是 null → 按"谁都没加好友、谁都没备注"处理）
    public System.Collections.Generic.Dictionary<string, RelationshipState> relationships { get; set; }
}

/// <summary>
/// 一个人和主角之间的关系账（存在存档里，跟着玩家的脚步变）
///
/// 【知识点 - 为什么字段名全小写】
/// 存档 JSON 里的键名要和这个类的属性名一字不差（System.Text.Json 读档时默认区分大小写）。
/// 上面 SaveData 的老字段都是小写开头，这里跟着同一套写法，
/// 玩家用记事本打开存档看得懂，手动改也不会改不动。
/// </summary>
public class RelationshipState
{
    /// <summary>加上好友了吗（第八章江洁靠这条决定她出不出现在会话列表）</summary>
    public bool friended { get; set; }

    /// <summary>玩家给他/她改的备注名；空串 = 没改过，显示时回落到联系人本名</summary>
    public string remark { get; set; } = "";

    /// <summary>关系阶段（just_met / friend / close / dating…）；空 = 用角色卡里的出厂值</summary>
    public string stage { get; set; } = "";

    /// <summary>关键节点：这个人身上发生过的事（ch08_first_chat、ch08_remark…），用来去重和后续章节判定</summary>
    public System.Collections.Generic.HashSet<string> keyEvents { get; set; } = new();
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
