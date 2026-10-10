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
    private HeartChips heartChips;
    private StatToast statToast;

    /// <summary>当前正在播放的章节 id（心声微选择记录用）</summary>
    private string currentChapterId = "";

    /// <summary>当前章节 id 只读暴露（故事时钟 / 内容 gating 用）</summary>
    public string CurrentChapterId => currentChapterId;

    // 全屏模态界面（比如手机微信）打开时的"输入抑制"：
    // 对话管理器不再抢点击/键盘，对话框也暂时藏起来，
    // 否则玩家点手机屏幕的按钮时，点击会被这里当成"推进对话"吃掉
    // （第六轮红框反馈"手机交互偏差、外面还能点"的根因）。
    private bool uiSuppressed;
    private string lastSpeaker = "";
    private string lastText = "";
    private string lastPortrait;

    /// <summary>是否有全屏模态界面（手机等）开着——章节里的散装热点用它挡误触</summary>
    public bool IsUiSuppressed => uiSuppressed;

    /// <summary>
    /// 打开/关闭全屏模态界面时调用：
    /// 抑制期间本管理器不吃任何输入、对话框隐藏；恢复时把当前那句对白原样显示回来
    /// </summary>
    public void SetUiSuppressed(bool value)
    {
        if (uiSuppressed == value)
            return;
        uiSuppressed = value;

        if (value)
        {
            dialogueBox?.HideBox();
        }
        else if (state == State.Typing || state == State.WaitingAdvance)
        {
            dialogueBox?.ShowLine(lastSpeaker, lastText, instant: true, portraitId: lastPortrait);
            dialogueBox?.SetContinueHintVisible(state == State.WaitingAdvance);
        }
    }

    /// <summary>数值飘字（选项之外的地方加了好感/勇气时也能提示玩家）</summary>
    public void ShowStatToast(int affectionDelta, int courageDelta)
    {
        statToast?.ShowDeltas(affectionDelta, courageDelta);
    }

    /// <summary>
    /// 取某组选项里第 index 个的文字（结束页"选择回顾"要用，也方便调试）。
    /// 心声微选择（heart_ 开头）会落到 hearts 字典里查。
    /// 找不到就返回空字符串，调用方自己决定怎么兜底。
    /// </summary>
    public string GetChoiceOptionText(string chapterId, string groupId, int index)
    {
        var data = LoadChapterData(chapterId);
        if (data == null)
            return "";

        // 心声微选择：choice id 形如 ch02_heart_window → 组名是 heart_window
        if (groupId != null && groupId.StartsWith("heart_"))
        {
            string heartId = groupId.Substring("heart_".Length);
            if (data.Hearts.TryGetValue(heartId, out var heart)
                && heart.Options != null && index >= 0 && index < heart.Options.Count)
                return heart.Options[index].Text;
            return "";
        }

        if (!data.Choices.TryGetValue(groupId, out var group))
            return "";
        if (group.Options == null || index < 0 || index >= group.Options.Count)
            return "";
        return group.Options[index].Text;
    }

    // 当前播放的语句队列
    private Queue<DialogueLine> lineQueue = new();
    private Action onSequenceFinished;

    // JSON 数据缓存：章节编号 → 解析好的数据（避免重复读文件）
    private readonly Dictionary<string, ChapterDialogueData> cache = new();

    /// <summary>是否有对话/选择正在进行（章节脚本用它防止重复触发）</summary>
    public bool IsBusy => state != State.Idle;

    // ==================== 第十一轮：快进 / 自动播放 / 优先点击 ====================

    /// <summary>
    /// "对话进行中也要能点"的控件白名单（各章节的返回按钮会注册进来）。
    /// 背景：_Input 比 GUI 更早收到事件，原来对话期间所有点击都被"推进对白"吃掉，
    /// 玩家点右上角的返回按钮怎么点都没反应（第十轮试玩反馈 B2）。
    /// </summary>
    private static readonly List<Control> priorityControls = new();

    /// <summary>把某个控件加入白名单（章节基类自动调用，不用手写）</summary>
    public static void RegisterPriorityControl(Control control)
    {
        if (control != null && !priorityControls.Contains(control))
            priorityControls.Add(control);
    }

    /// <summary>把某个控件移出白名单（场景销毁时自动调用）</summary>
    public static void UnregisterPriorityControl(Control control)
    {
        priorityControls.Remove(control);
    }

    /// <summary>这个点是不是落在白名单控件上？是的话返回那个控件</summary>
    private static Control FindPriorityControl(Vector2 globalPos)
    {
        for (int i = priorityControls.Count - 1; i >= 0; i--)
        {
            var c = priorityControls[i];
            if (!GodotObject.IsInstanceValid(c) || !c.IsVisibleInTree())
            {
                priorityControls.RemoveAt(i); // 场景已切走，顺手清掉
                continue;
            }
            // 外扩 8px：手指点得稍微偏一点也算数
            if (c.GetGlobalRect().Grow(8).HasPoint(globalPos))
                return c;
        }
        return null;
    }

    /// <summary>自动播放开关（Tab 切换）</summary>
    public bool AutoPlay { get; private set; }

    /// <summary>按住 Ctrl = 快进（字瞬间出全，句与句之间几乎不停）</summary>
    private const double FastForwardDwell = 0.18;

    // 自动播放 / 快进的停留计时器
    private double holdTimer;

    public void ToggleAutoPlay()
    {
        AutoPlay = !AutoPlay;
        holdTimer = 0;
        dialogueBox?.SetAutoBadgeVisible(AutoPlay);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxClick, -16f);
        GD.Print($"[对话] 自动播放：{(AutoPlay ? "开" : "关")}");
    }

    /// <summary>
    /// 每帧驱动：① 按住 Ctrl 快进；② 开了自动播放就按语速自动往下走。
    /// 手机/确认弹窗打开时（uiSuppressed）一律不插手，免得和模态界面抢输入。
    /// </summary>
    public override void _Process(double delta)
    {
        if (uiSuppressed)
            return;

        // 还在逐字显示：按住 Ctrl 就把这一句全部显示出来
        if (state == State.Typing && Input.IsKeyPressed(Key.Ctrl))
        {
            dialogueBox.CompleteTyping();
            return;
        }

        if (state != State.WaitingAdvance)
        {
            holdTimer = 0;
            return;
        }

        bool fastForward = Input.IsKeyPressed(Key.Ctrl);
        if (!fastForward && !AutoPlay)
        {
            holdTimer = 0;
            return;
        }

        holdTimer += delta;
        double dwell = fastForward
            ? FastForwardDwell
            : GameSettings.Instance?.AutoAdvanceDelay(lastText?.Length ?? 0) ?? 2.0;

        if (holdTimer >= dwell)
        {
            holdTimer = 0;
            ShowNextLine();
        }
    }

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

        // 心声微选择的小纸片（第十二轮）：纯代码搭建，和选项面板同层（两者不会同时出现）
        // （纯代码 new 出来的节点默认叫 "@Control@N"，显式命名方便调试与自动化测试查找）
        heartChips = new HeartChips();
        heartChips.Name = "HeartChips";
        AddChild(heartChips);

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

        currentChapterId = chapterId;
        lineQueue = new Queue<DialogueLine>(lines);
        onSequenceFinished = onFinished;
        ShowNextLine();
    }

    /// <summary>
    /// 直接播一小组对白（第十二轮"点万物有回应"用）：
    /// 章节外的散装对白不存在 JSON 序列里，调用方自己拿着行列表进来。
    /// </summary>
    public void PlayLines(List<DialogueLine> lines, Action onFinished = null)
    {
        if (lines == null || lines.Count == 0)
        {
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
        lastSpeaker = group.PromptSpeaker;
        lastText = group.Prompt;
        lastPortrait = group.PromptPortrait;
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
        heartChips?.HideAll();
        statToast?.HideToast();
    }

    // ==================== 内部逻辑 ====================

    /// <summary>
    /// 显示队列里的下一条；队列空了就结束序列。
    /// 行里带着 heartChoice 时：先弹心声小纸片，选完再把玩家选的分支对白插回队列前面。
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

        // 心声微选择：这一步不是"一句话"，而是一个小小的岔路口
        if (!string.IsNullOrEmpty(line.HeartChoice))
        {
            StartHeartChoice(line.HeartChoice);
            return;
        }

        state = State.Typing;
        lastSpeaker = line.Speaker;
        lastText = line.Text;
        lastPortrait = line.Portrait;
        dialogueBox.ShowLine(line.Speaker, line.Text, portraitId: line.Portrait);
    }

    /// <summary>
    /// 心声微选择：提示语进对话框，选项做成底部的小纸片。
    /// 选择不改好感/勇气（防止微选择变成刷分工具），只记录 + 播分支对白。
    /// </summary>
    private void StartHeartChoice(string heartId)
    {
        var data = LoadChapterData(currentChapterId);
        if (data == null || !data.Hearts.TryGetValue(heartId, out var heart) || heart.Options is not { Count: > 0 })
        {
            GD.PrintErr($"[对话] 找不到心声选择:{currentChapterId} / {heartId}");
            ShowNextLine();
            return;
        }

        state = State.WaitingChoice;

        string speaker = string.IsNullOrEmpty(heart.PromptSpeaker) ? "我" : heart.PromptSpeaker;
        lastSpeaker = speaker;
        lastText = heart.Prompt;
        lastPortrait = null;
        dialogueBox.ShowLine(speaker, heart.Prompt, instant: true);
        dialogueBox.SetContinueHintVisible(false);

        heartChips.ShowChoices(heart.Options, index =>
        {
            var option = heart.Options[index];

            // 记录这条心声（结束页"选择回顾"会用到），但不加好感/勇气
            GameManager.Instance.RecordChoice($"{currentChapterId}_heart_{heartId}", index);
            heartChips.HideAll();

            // 玩家选的分支对白插到剩余队列的最前面，接着往下播
            var rest = lineQueue;
            lineQueue = new Queue<DialogueLine>(option.Lines ?? new List<DialogueLine>());
            foreach (var l in rest)
                lineQueue.Enqueue(l);

            ShowNextLine();
        });
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
    ///
    /// 【第十一轮】两条新规矩：
    ///   1) Tab 随时可以切换自动播放（打字机太磨人的玩家福音）；
    ///   2) 白名单控件（返回按钮）所在的点击不"吃"，直接触发它 ——
    ///      这样对话播到一半也能点返回，不会再出现"怎么点都没反应"。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (uiSuppressed) // 手机等全屏模态界面打开时，输入全部交给它们
            return;

        // ---- Tab：切换自动播放（任何对话状态下都可以）----
        if (@event is InputEventKey tabKey
            && tabKey.Pressed && !tabKey.Echo && tabKey.Keycode == Key.Tab)
        {
            GetViewport().SetInputAsHandled();
            ToggleAutoPlay();
            return;
        }

        if (state == State.Idle)
            return;

        // ---- 白名单控件优先：点到了就直接"按下"它，不当成推进对白 ----
        if (@event is InputEventMouseButton pm && pm.Pressed
            && pm.ButtonIndex == MouseButton.Left)
        {
            var priority = FindPriorityControl(pm.Position);
            if (priority != null)
            {
                GetViewport().SetInputAsHandled();
                // 相当于玩家真的用鼠标按了一下这个按钮
                priority.EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }
        }

        // 选择阶段：点在选项按钮 / 心声小纸片上就"放行"，让按钮自己收到这一击；
        // 只有点空白处才吞掉，防止误触场景里的热点。
        // （第十二轮修的回归 bug：这里原来一律吞掉 —— 在 Godot 里 _Input 比 GUI 按钮先拿到事件，
        //   结果所有选项都点不动、只能靠键盘或代码兜底。真点击必须放给按钮。）
        if (state == State.WaitingChoice)
        {
            if (@event is InputEventMouseButton blank && blank.Pressed
                && blank.ButtonIndex == MouseButton.Left
                && !choicePanel.HasButtonAt(blank.Position)
                && !(heartChips != null && heartChips.HasChipAt(blank.Position)))
                GetViewport().SetInputAsHandled();
            return;
        }

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

    // 心声微选择：非空时这一"行"不是台词，而是一个小小的岔路口（值对应 hearts 里的键）
    public string HeartChoice { get; set; } = "";
}

/// <summary>一个章节的全部剧情数据（对应一个 JSON 文件）</summary>
public class ChapterDialogueData
{
    public Dictionary<string, List<DialogueLine>> Sequences { get; set; }
    public Dictionary<string, ChoiceGroupData> Choices { get; set; }

    /// <summary>心声微选择（第十二轮）：小岔路口，不影响好感/勇气，只记录 + 分支对白</summary>
    public Dictionary<string, HeartChoiceData> Hearts { get; set; }

    public void EnsureInitialized()
    {
        Sequences ??= new();
        Choices ??= new();
        Hearts ??= new();
    }
}

/// <summary>一组心声微选择：一句提示 + 两三个小纸片</summary>
public class HeartChoiceData
{
    public string Prompt { get; set; } = "";         // 提示语（显示在对话框里）
    public string PromptSpeaker { get; set; } = "";  // 提示语说话人（不写 = "我"）
    public List<HeartOptionData> Options { get; set; } = new();
}

/// <summary>一片心声小纸片：文案 + 选完要播的分支对白</summary>
public class HeartOptionData
{
    public string Text { get; set; } = "";
    public List<DialogueLine> Lines { get; set; } = new();
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
