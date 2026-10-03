using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 对话管理器（自动加载单例）
///
/// 负责把 JSON 里的剧情文字一句一句显示出来：
///   - 打字机效果（DialogueBox 负责）
///   - 点击/空格推进到下一条
///   - 选项面板弹出与回调
///   - 属性变化自动应用（选项数据里带着好感度/勇气值变化）
///
/// 【知识点 - 数据驱动设计】
/// 剧情文字放在 data/dialogues/章节名.json 里，
/// 改台词只需要改 JSON 文件，不用碰代码！
/// </summary>
public partial class DialogueManager : CanvasLayer
{
    public static DialogueManager Instance { get; private set; }

    // 对话状态机
    private enum State
    {
        Idle,            // 空闲（没有对话进行中）
        Typing,          // 正在逐字显示
        WaitingAdvance,  // 显示完了，等玩家点击继续
        WaitingChoice    // 等玩家做选择
    }

    private State state = State.Idle;

    private DialogueBox dialogueBox;
    private ChoicePanel choicePanel;
    private StatToast statToast;

    // 当前播放的语句队列
    private Queue<DialogueLine> lineQueue = new();
    private Action onSequenceFinished;

    // JSON 数据缓存：章节编号 → 解析好的数据（避免重复读文件）
    private readonly Dictionary<string, ChapterDialogueData> cache = new();

    /// <summary>是否有对话/选择正在进行（章节脚本用它防止重复触发）</summary>
    public bool IsBusy => state != State.Idle;

    public override void _Ready()
    {
        Instance = this;
        Layer = 50; // 在游戏画面之上、过渡黑幕(100)和调试面板(99)之下

        // 实例化对话框 UI
        var boxScene = GD.Load<PackedScene>("res://scenes/ui/dialogue_box/dialogue_box.tscn");
        dialogueBox = boxScene.Instantiate<DialogueBox>();
        AddChild(dialogueBox);
        dialogueBox.TypewriterFinished += OnTypewriterFinished;

        // 实例化选项面板 UI
        var choiceScene = GD.Load<PackedScene>("res://scenes/ui/choice_panel/choice_panel.tscn");
        choicePanel = choiceScene.Instantiate<ChoicePanel>();
        AddChild(choicePanel);

        // 实例化数值飘字 UI（加在最后 → 显示在最上层）
        var toastScene = GD.Load<PackedScene>("res://scenes/ui/stat_toast/stat_toast.tscn");
        statToast = toastScene.Instantiate<StatToast>();
        AddChild(statToast);

        GD.Print("[对话系统] 已加载");
    }

    // ==================== 对外 API（章节脚本调用这两个方法） ====================

    /// <summary>
    /// 播放一段剧情序列。
    /// </summary>
    /// <param name="chapterId">章节名，对应 data/dialogues/{chapterId}.json</param>
    /// <param name="sequenceId">序列名，对应 JSON 里 sequences 下的键</param>
    /// <param name="onFinished">播完后要做的事（可省略）</param>
    public void PlaySequence(string chapterId, string sequenceId, Action onFinished = null)
    {
        var data = LoadChapterData(chapterId);
        if (data == null || !data.Sequences.TryGetValue(sequenceId, out var lines))
        {
            GD.PrintErr($"[对话] 找不到序列：{chapterId} / {sequenceId}");
            onFinished?.Invoke();
            return;
        }

        lineQueue = new Queue<DialogueLine>(lines);
        onSequenceFinished = onFinished;
        ShowNextLine();
    }

    /// <summary>
    /// 弹出一组选项。提示语显示在对话框里，选项按钮显示在选项面板上。
    /// 玩家选择后：自动应用属性变化、记录选择、然后调用回调。
    /// </summary>
    /// <param name="chapterId">章节名</param>
    /// <param name="choiceGroupId">选项组名，对应 JSON 里 choices 下的键</param>
    /// <param name="onSelected">选完后要做的事，参数是选了第几个（从 0 开始）</param>
    public void ShowChoice(string chapterId, string choiceGroupId, Action<int> onSelected = null)
    {
        var data = LoadChapterData(chapterId);
        if (data == null || !data.Choices.TryGetValue(choiceGroupId, out var group))
        {
            GD.PrintErr($"[对话] 找不到选项组：{chapterId} / {choiceGroupId}");
            onSelected?.Invoke(0);
            return;
        }

        state = State.WaitingChoice;

        // 提示语直接显示（不做打字机，等玩家看选项）
        dialogueBox.ShowLine(group.PromptSpeaker, group.Prompt, instant: true, portraitId: group.PromptPortrait);
        dialogueBox.SetContinueHintVisible(false); // 选项阶段不需要"点击继续"

        choicePanel.ShowChoices(group.Options, index =>
        {
            var option = group.Options[index];

            // 应用属性变化
            if (option.Affection != 0) GameManager.Instance.AddAffection(option.Affection);
            if (option.Courage != 0) GameManager.Instance.AddCourage(option.Courage);

            // 数值飘字：让玩家立刻看到"这个选择有分量"
            statToast?.ShowDeltas(option.Affection, option.Courage);

            // 记录选择（结局判定会用到）
            GameManager.Instance.RecordChoice($"{chapterId}_{choiceGroupId}", index);

            state = State.Idle;
            onSelected?.Invoke(index);
        });
    }

    /// <summary>
    /// 强制停止所有对话（场景切换、返回菜单时调用）
    /// </summary>
    public void Stop()
    {
        lineQueue.Clear();
        onSequenceFinished = null;
        state = State.Idle;
        dialogueBox?.HideBox();
        choicePanel?.Hide();
        statToast?.HideToast();
    }

    // ==================== 内部逻辑 ====================

    /// <summary>
    /// 显示队列里的下一条；队列空了就结束序列
    /// </summary>
    private void ShowNextLine()
    {
        if (lineQueue.Count == 0)
        {
            state = State.Idle;
            dialogueBox.HideBox();

            var callback = onSequenceFinished;
            onSequenceFinished = null;
            callback?.Invoke();
            return;
        }

        var line = lineQueue.Dequeue();
        state = State.Typing;
        dialogueBox.ShowLine(line.Speaker, line.Text, portraitId: line.Portrait);
    }

    /// <summary>
    /// 打字机播完一行的回调
    /// </summary>
    private void OnTypewriterFinished()
    {
        if (state == State.Typing)
        {
            state = State.WaitingAdvance;
        }
    }

    /// <summary>
    /// 全局输入：对话进行中时，点击/空格/回车 = 推进
    /// 【知识点 - _Input 与 SetInputAsHandled】
    /// _Input 比 UI 按钮更早收到事件；
    /// SetInputAsHandled() 表示"这个事件我处理了，别再传给按钮"，
    /// 这样对话时点击不会误触场景里的热点。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (state != State.Typing && state != State.WaitingAdvance)
            return;

        bool advance = false;

        if (@event is InputEventMouseButton mouse
            && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            advance = true;
        }
        else if (@event is InputEventKey key
            && key.Pressed && !key.Echo
            && (key.Keycode == Key.Space || key.Keycode == Key.Enter))
        {
            advance = true;
        }

        if (!advance) return;

        GetViewport().SetInputAsHandled();

        // 推进对话的"咔"声
        AudioManager.Instance?.PlaySfx(AudioManager.SfxClick, -14f);

        if (state == State.Typing)
        {
            // 还没显示完 → 直接跳到全文（省时间）
            dialogueBox.CompleteTyping();
        }
        else
        {
            // 显示完了 → 下一句
            ShowNextLine();
        }
    }

    /// <summary>
    /// 读取并解析章节的剧情 JSON（带缓存）
    /// </summary>
    private ChapterDialogueData LoadChapterData(string chapterId)
    {
        if (cache.TryGetValue(chapterId, out var cached))
            return cached;

        string path = $"res://data/dialogues/{chapterId}.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[对话] 找不到剧情文件：{path}");
            return null;
        }

        string json = FileAccess.GetFileAsString(path);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<ChapterDialogueData>(json, options);
            data?.EnsureInitialized();
            if (data != null)
                cache[chapterId] = data;
            return data;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[对话] JSON 解析失败 {path}：{e.Message}");
            return null;
        }
    }
}

// ==================== 剧情数据结构 ====================

/// <summary>一条对话</summary>
public class DialogueLine
{
    public string Speaker { get; set; } = ""; // 说话人（空 = 旁白）
    public string Text { get; set; } = "";    // 文字内容

    // 立绘编号（比如 "roommate_smile"）。
    // JSON 里不写 = null → 保持当前立绘；写 "" = 收起立绘；写编号 = 换立绘
    public string Portrait { get; set; } = null;
}

/// <summary>一个章节的全部剧情数据（对应一个 JSON 文件）</summary>
public class ChapterDialogueData
{
    public Dictionary<string, List<DialogueLine>> Sequences { get; set; }
    public Dictionary<string, ChoiceGroupData> Choices { get; set; }

    public void EnsureInitialized()
    {
        Sequences ??= new();
        Choices ??= new();
    }
}

/// <summary>一组选项</summary>
public class ChoiceGroupData
{
    public string Prompt { get; set; } = "";        // 提示语（显示在对话框里）
    public string PromptSpeaker { get; set; } = ""; // 提示语说话人
    public string PromptPortrait { get; set; } = null; // 提示语立绘（不写 = 保持当前）
    public List<ChoiceOptionData> Options { get; set; } = new();
}

/// <summary>一个选项</summary>
public class ChoiceOptionData
{
    public string Text { get; set; } = "";   // 选项文字
    public int Affection { get; set; } = 0;  // 好感度变化
    public int Courage { get; set; } = 0;    // 勇气值变化
}
