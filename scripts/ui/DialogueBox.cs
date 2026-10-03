using Godot;
using System;

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

    private float charsShown = 0f;
    private bool typing = false;

    public bool IsTyping => typing;

    public override void _Ready()
    {
        nameLabel = GetNode<Label>("Panel/VBox/NameLabel");
        textLabel = GetNode<RichTextLabel>("Panel/VBox/TextLabel");
        continueHint = GetNode<Label>("Panel/VBox/ContinueHint");

        Hide(); // 默认隐藏，需要时再显示
    }

    /// <summary>
    /// 显示一句话（开始打字机）
    /// </summary>
    public void ShowLine(string speaker, string text, bool instant = false)
    {
        Show();

        nameLabel.Text = speaker; // 空字符串 = 旁白（没有名字）

        textLabel.Text = text;
        textLabel.VisibleCharacters = 0;
        charsShown = 0f;
        typing = true;
        continueHint.Hide();

        if (instant)
        {
            CompleteTyping();
        }
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
            // 逐帧增加显示字数
            int total = textLabel.GetTotalCharacterCount();
            charsShown += CharsPerSecond * (float)delta;

            if (charsShown >= total)
            {
                CompleteTyping();
            }
            else
            {
                textLabel.VisibleCharacters = (int)charsShown;
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
