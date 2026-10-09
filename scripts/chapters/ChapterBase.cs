using Godot;
using System;

/// <summary>
/// 章节基类 —— 所有章节都继承这个类
///
/// 【Day 2 核心知识点 - C# 继承】
/// 继承就像"模板"：
///   - ChapterBase 定义了所有章节共有的功能（标题、返回按钮、自动存档）
///   - 具体章节（Prologue、Chapter01...）继承它，只需写自己特有的内容
///   - 就像"人类"定义了"会呼吸"，"学生"继承"人类"后还多了"会学习"
///
/// 使用方法：
///   1. 创建新章节脚本时，把 : Control 改成 : ChapterBase
///   2. 重写 OnChapterReady() 写章节特有逻辑
///   3. 在 .tscn 中设置 ChapterTitle 和 ChapterSubtitle
/// </summary>
public partial class ChapterBase : Control
{
    // ========== 可在编辑器中设置的参数 ==========
    // [Export] 让变量出现在 Godot 编辑器的属性面板中
    [Export]
    public string ChapterTitle { get; set; } = "";  // 章节标题

    [Export]
    public string ChapterSubtitle { get; set; } = ""; // 章节副标题

    [Export]
    public int ChapterIndex { get; set; } = 0;      // 章节编号

    // ========== 节点引用（子类可以访问） ==========
    protected Label titleLabel;    // 标题文字
    protected Label subtitleLabel; // 副标题
    protected Button backButton;   // 返回菜单按钮
    protected Button bagButton;    // 背包按钮（第十五轮：左上角常驻）

    // ========== Godot 生命周期 ==========

    /// <summary>
    /// _Ready：节点第一次出现时调用
    /// 这里做通用初始化，子类不需要重复写
    /// </summary>
    public override void _Ready()
    {
        // 查找场景中的节点（用 GetNodeOrNull 避免找不到时报错）
        titleLabel = GetNodeOrNull<Label>("ChapterTitle");
        subtitleLabel = GetNodeOrNull<Label>("ChapterSubtitle");
        backButton = GetNodeOrNull<Button>("BackButton");

        // 设置标题文字
        if (titleLabel != null && !string.IsNullOrEmpty(ChapterTitle))
        {
            titleLabel.Text = ChapterTitle;
        }
        if (subtitleLabel != null && !string.IsNullOrEmpty(ChapterSubtitle))
        {
            subtitleLabel.Text = ChapterSubtitle;
        }

        // 绑定返回按钮
        if (backButton != null)
        {
            backButton.Pressed += OnBackPressed;
            // 注册成"对话期间也能点"的白名单（试玩反馈 B2：对话中点返回没反应）
            DialogueManager.RegisterPriorityControl(backButton);
        }

        // 左上角的背包按钮（第十五轮：手机/捡到的东西随时能翻出来看）
        BuildBagButton();

        // 更新章节编号
        GameManager.Instance.CurrentChapter = ChapterIndex;

        // 自动存档（每章开始时保存一次）
        GameManager.Instance.SaveGame();

        GD.Print($"[章节] {ChapterTitle} 已加载 (第 {ChapterIndex} 章)");

        // 调用子类的自定义初始化（虚方法，子类可以重写）
        OnChapterReady();
    }

    /// <summary>
    /// 子类重写这个方法，写章节特有的初始化逻辑
    /// 【Day 2 知识点 - virtual 虚方法】
    /// virtual = "这个方法子类可以覆盖成自己的版本"
    /// </summary>
    protected virtual void OnChapterReady()
    {
        // 基类什么都不做，留给子类实现
    }

    /// <summary>离开场景树（切章/回主菜单）时，把白名单里的返回按钮摘掉，避免留下野指针</summary>
    public override void _ExitTree()
    {
        if (backButton != null)
            DialogueManager.UnregisterPriorityControl(backButton);
        if (bagButton != null)
            DialogueManager.UnregisterPriorityControl(bagButton);
    }

    // ========== 背包（第十五轮） ==========

    /// <summary>
    /// 左上角搭一个"背包"按钮：样式和右上角的返回按钮是一家（琥珀色圆角）。
    /// 注册进"对话期间也能点"的白名单 —— 手机是随时可看的物品，
    /// 对话播到一半也应该能翻背包（试玩反馈：手机要随时能拿出来）。
    /// 按钮右上角挂一个小红点：背包里有没看过的新东西时自己亮起来。
    /// </summary>
    private void BuildBagButton()
    {
        bagButton = new Button
        {
            Name = "BagButton", // 纯代码 new 默认叫 "@Button@N"，显式命名方便调试与自动化测试
            Text = DataStore.Text("bag.button"),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        bagButton.AnchorLeft = 0f; bagButton.AnchorRight = 0f;
        bagButton.AnchorTop = 0f; bagButton.AnchorBottom = 0f;
        bagButton.OffsetLeft = 24; bagButton.OffsetTop = 24;
        bagButton.OffsetRight = 132; bagButton.OffsetBottom = 68;
        bagButton.AddThemeFontSizeOverride("font_size", 18);
        bagButton.AddThemeStyleboxOverride("normal", HudButtonStyle(new Color(0.831f, 0.647f, 0.455f)));
        bagButton.AddThemeStyleboxOverride("hover", HudButtonStyle(new Color(0.910f, 0.769f, 0.604f)));
        bagButton.AddThemeStyleboxOverride("pressed", HudButtonStyle(new Color(0.722f, 0.537f, 0.306f)));
        AddChild(bagButton);

        bagButton.Pressed += OpenBag;
        DialogueManager.RegisterPriorityControl(bagButton);
        UiSounds.Wire(bagButton);

        var dot = new ChapterHudDot();
        dot.Name = "BagNewDot";
        bagButton.AddChild(dot);
    }

    private static StyleBoxFlat HudButtonStyle(Color bg)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
        };
    }

    /// <summary>翻开背包（全屏面板；打开期间背景对话/热点被锁死，和手机一个待遇）</summary>
    protected void OpenBag()
    {
        if (DialogueManager.Instance?.IsUiSuppressed == true)
            return; // 手机/背包已经开着一个了

        var bag = new Backpack();
        AddChild(bag);
        bag.Open();
    }

    /// <summary>
    /// 返回按钮被按下时调用
    ///
    /// 【第十一轮改动】原来点一下就当场切回主菜单，试玩反馈里被吐槽"手滑一次进度演出全丢"。
    /// 现在先弹一个确认框（ConfirmHost），玩家真的想走才走。
    /// </summary>
    protected virtual void OnBackPressed()
    {
        ConfirmHost.Ask("confirm.back_title", "confirm.back_body", DoLeaveChapter);
    }

    /// <summary>确认离开之后真正做的事：停对话 → 存档 → 回主菜单</summary>
    private void DoLeaveChapter()
    {
        // 停掉可能正在播的对话，再存档返回
        DialogueManager.Instance?.Stop();
        GameManager.Instance.SaveGame();
        GameManager.Instance.ReturnToMainMenu();
    }

    // ========== 给子类提供的便捷方法 ==========

    /// <summary>
    /// 增加好感度（子类直接调用即可）
    /// </summary>
    protected void AddAffection(int value)
    {
        GameManager.Instance.AddAffection(value);
    }

    /// <summary>
    /// 增加勇气值
    /// </summary>
    protected void AddCourage(int value)
    {
        GameManager.Instance.AddCourage(value);
    }

    /// <summary>
    /// 记录一个选择
    /// </summary>
    protected void RecordChoice(string choiceId, int optionIndex)
    {
        GameManager.Instance.RecordChoice(choiceId, optionIndex);
    }

    /// <summary>
    /// 找到隐藏物品（itemId 对应 data/hidden_items.json；同一个 id 只记一次）
    /// </summary>
    protected void FindHiddenItem(string itemId = "")
    {
        GameManager.Instance.FoundHiddenItem(itemId);
    }

    /// <summary>
    /// 进入下一章（带过渡效果）
    /// </summary>
    protected void GoToNextChapter(string nextScenePath, int nextChapterIndex)
    {
        // 先把章节号写进内存再存档：不然存进去的还是"当前章"，
        // 通关标记（99）永远落不了盘，主菜单"继续游戏"只能靠兜底猜（r14 回归抓到）
        GameManager.Instance.CurrentChapter = nextChapterIndex;
        GameManager.Instance.SaveGame();
        GameManager.Instance.ChangeSceneWithTransition(nextScenePath, nextChapterIndex);
    }
}
