using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 对话框 UI —— 负责"显示"这一件事
///
/// 【知识点 - 打字机效果】
/// RichTextLabel 的 VisibleCharacters 属性控制"显示前几个字"：
///   - 设为 0    → 一个字都不显示
///   - 设为 10   → 只显示前 10 个字
///   - 设为 -1   → 全部显示
/// 我们在每帧(_Process)里让这个数字慢慢变大，就形成了打字效果。
/// 
/// 分工：DialogueBox 只管显示，DialogueManager 管流程逻辑。
/// </summary>
public partial class DialogueBox : Control
{
    /// <summary>一行打完时发出这个信号，DialogueManager 收到后切换状态</summary>
    [Signal]
    public delegate void TypewriterFinishedEventHandler();

    /// <summary>打字速度：每秒显示多少个字</summary>
    [Export]
    public float CharsPerSecond { get; set; } = 28f;

    private Label nameLabel;
    private RichTextLabel textLabel;
    private Label continueHint;
    private Label autoBadge;   // "自动播放中"小角标（第十一轮）
    private Control panel;

    // 立绘（圆形头像）
    private Control portraitRoot;
    private TextureRect portraitRect;
    private Tween portraitTween;
    private string currentPortraitId = "";

    // 立绘贴图缓存：id → 贴图（避免每次说话都重新读盘）
    private static readonly Dictionary<string, Texture2D> portraitCache = new();

    private float charsShown = 0f;
    private bool typing = false;

    // 打字机音效：每显示 N 个字"滴答"一声
    private const int TickEveryChars = 3;
    private int nextTickChar;

    // 弹出动画的基准位置（第一次显示时记录）
    private Vector2 panelBasePos;
    private bool panelBaseCaptured;

    public bool IsTyping => typing;

    public override void _Ready()
    {
        panel = GetNode<Control>("Panel");
        nameLabel = GetNode<Label>("Panel/VBox/NameLabel");
        textLabel = GetNode<RichTextLabel>("Panel/VBox/TextLabel");
        continueHint = GetNode<Label>("Panel/VBox/ContinueHint");
        autoBadge = GetNode<Label>("AutoBadge");
        portraitRoot = GetNode<Control>("PortraitRoot");
        portraitRect = GetNode<TextureRect>("PortraitRoot/Portrait");

        // 文案搬到 data/text/ui_text.json，想改字不用回这里（试玩反馈 C6）
        continueHint.Text = DataStore.Text("ui.continue_hint");
        autoBadge.Text = DataStore.Text("ui.auto_badge");

        Hide(); // 默认隐藏，需要时再显示
    }

    /// <summary>显示/隐藏"自动播放中"角标（Tab 切换时由 DialogueManager 调用）</summary>
    public void SetAutoBadgeVisible(bool visible)
    {
        autoBadge.Visible = visible;
    }

    /// <summary>
    /// 显示一句话（开始打字机）
    /// </summary>
    /// <param name="portraitId">立绘编号，比如 "roommate_smile"。
    ///   传 null（默认）= 保持当前立绘不变；传 "" = 收起立绘；传编号 = 换成立绘。</param>
    public void ShowLine(string speaker, string text, bool instant = false, string portraitId = null)
    {
        bool wasHidden = !Visible; // 之前藏着吗？（决定要不要播弹出动画）
        Show();

        if (wasHidden)
            PlayPopIn();

        // 立绘在对话框之前处理，这样淡入动画和打字机同时进行
        if (portraitId != null)
            SetPortrait(portraitId);

        nameLabel.Text = speaker; // 空字符串 = 旁白（没有名字）

        textLabel.Text = text;
        textLabel.VisibleCharacters = 0;
        charsShown = 0f;
        typing = true;
        nextTickChar = TickEveryChars;
        continueHint.Hide();

        if (instant)
        {
            CompleteTyping();
        }
    }

    /// <summary>
    /// 切换立绘（内部方法）
    /// </summary>
    private void SetPortrait(string id)
    {
        if (id == currentPortraitId && portraitRoot.Visible)
            return; // 没变化，不折腾

        // 空字符串 = 收起立绘
        if (string.IsNullOrEmpty(id))
        {
            currentPortraitId = "";
            portraitRoot.Visible = false;
            return;
        }

        // 从缓存或磁盘取贴图
        if (!portraitCache.TryGetValue(id, out var texture))
        {
            string path = $"res://assets/portraits/{id}.png";
            texture = GD.Load<Texture2D>(path);
            if (texture == null)
            {
                GD.PrintErr($"[对话] 找不到立绘：{path}");
                currentPortraitId = "";
                portraitRoot.Visible = false;
                return;
            }
            portraitCache[id] = texture;
        }

        currentPortraitId = id;
        portraitRect.Texture = texture;
        portraitRoot.Visible = true;

        // 淡入（换表情也有一个柔和的过渡）
        portraitTween?.Kill();
        portraitRoot.Modulate = new Color(1, 1, 1, 0);
        portraitTween = CreateTween();
        portraitTween.TweenProperty(portraitRoot, "modulate:a", 1.0f, 0.18f);
    }

    /// <summary>
    /// 小演出：对话框从下方轻轻滑出来的弹出动画
    /// </summary>
    private void PlayPopIn()
    {
        if (!panelBaseCaptured)
        {
            panelBasePos = panel.Position;
            panelBaseCaptured = true;
        }

        panel.Position = panelBasePos + new Vector2(0, 20);
        panel.Modulate = new Color(1, 1, 1, 0);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(panel, "position", panelBasePos, 0.22f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(panel, "modulate:a", 1.0f, 0.18f);
    }

    /// <summary>
    /// 立即显示整句（点击时跳过打字机）
    /// </summary>
    public void CompleteTyping()
    {
        if (!typing) return;

        typing = false;
        textLabel.VisibleCharacters = -1; // -1 = 全部显示
        continueHint.Show();
        EmitSignal(SignalName.TypewriterFinished);
    }

    /// <summary>
    /// 隐藏对话框
    /// </summary>
    public void HideBox()
    {
        typing = false;
        Hide();

        // 对话框收起时，立绘也跟着收起
        portraitTween?.Kill();
        portraitRoot.Visible = false;
        currentPortraitId = "";
    }

    /// <summary>
    /// 单独控制"点击继续"提示的显示（选项阶段把它藏起来）
    /// </summary>
    public void SetContinueHintVisible(bool visible)
    {
        continueHint.Visible = visible;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        if (typing)
        {
            // 逐帧增加显示字数（速度来自设置页，玩家可快可慢）
            float cps = GameSettings.Instance != null
                ? GameSettings.Instance.CharsPerSecond
                : CharsPerSecond;
            int total = textLabel.GetTotalCharacterCount();
            charsShown += cps * (float)delta;

            if (charsShown >= total)
            {
                CompleteTyping();
            }
            else
            {
                textLabel.VisibleCharacters = (int)charsShown;

                // 打字机"滴答"音效：每 TickEveryChars 个字一声，音高小幅随机（避免机械感）
                if ((int)charsShown >= nextTickChar)
                {
                    AudioManager.Instance?.PlaySfx(AudioManager.SfxTick, -20f, 0.07f);
                    nextTickChar = (int)charsShown + TickEveryChars;
                }
            }
        }
        else if (continueHint.Visible)
        {
            // "点击继续"提示呼吸闪烁
            float alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.004f));
            continueHint.Modulate = new Color(1, 1, 1, alpha);
        }
    }
}
