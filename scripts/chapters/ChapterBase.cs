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
        GameManager.Instance.SaveGame();
        GameManager.Instance.ChangeSceneWithTransition(nextScenePath, nextChapterIndex);
    }
}
