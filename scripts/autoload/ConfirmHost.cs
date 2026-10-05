using Godot;
using System;

/// <summary>
/// 二次确认弹窗（自动加载单例）
///
/// 【为什么需要它】
/// 第十轮试玩反馈：点「开始游戏」当场把旧存档抹掉、章节里点「返回菜单」一句话不说就走了。
/// 这种"不可逆"的操作必须给玩家一个反悔的机会。
///
/// 【用法】任何地方一行代码就能弹：
///   ConfirmHost.Ask("confirm.newgame_title", "confirm.newgame_body", () => 真正要做的事());
/// 标题和正文都传文案的键（见 data/text/ui_text.json），改文案不用碰代码。
///
/// 【知识点 - CanvasLayer 层级】
/// 本弹窗在 90 层：对话框(50) 之上、调试面板(99)、黑幕过渡(100) 之下，
/// 所以弹出来时一定盖住对话，但转场黑幕依然能盖住它。
/// </summary>
public partial class ConfirmHost : CanvasLayer
{
    public static ConfirmHost Instance { get; private set; }

    private Control root;        // 全屏遮罩（点空白处 = 取消）
    private Label titleLabel;
    private Label bodyLabel;
    private Button okButton;
    private Button cancelButton;

    private Action onConfirm;
    private Action onCancel;

    /// <summary>当前是否正显示着弹窗（对话系统用它决定要不要抢点击）</summary>
    public bool IsActive => root != null && root.Visible;

    public override void _Ready()
    {
        Instance = this;
        Layer = 90;

        // ---- 全屏半透明遮罩：挡住下面所有按钮 ----
        root = new ColorRect
        {
            Color = new Color(0.02f, 0.02f, 0.05f, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        ((ColorRect)root).SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // ---- 居中的纸片面板 ----
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", MakePaper());
        center.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 46);
        margin.AddThemeConstantOverride("margin_right", 46);
        margin.AddThemeConstantOverride("margin_top", 34);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        margin.AddChild(box);

        titleLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(720, 0),
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 34);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.28f, 0.18f, 0.09f));
        box.AddChild(titleLabel);

        bodyLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(720, 0),
        };
        bodyLabel.AddThemeFontSizeOverride("font_size", 22);
        bodyLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.32f, 0.2f));
        box.AddChild(bodyLabel);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(720, 0),
        };
        buttons.AddThemeConstantOverride("separation", 28);
        box.AddChild(buttons);

        okButton = MakeButton("confirm.newgame_yes");
        cancelButton = MakeButton("confirm.newgame_no");
        buttons.AddChild(okButton);
        buttons.AddChild(cancelButton);

        okButton.Pressed += () => Finish(confirmed: true);
        cancelButton.Pressed += () => Finish(confirmed: false);
        ((ColorRect)root).GuiInput += @event =>
        {
            // 点面板外的空白 = 取消（和多数游戏的习惯一致）
            if (@event is InputEventMouseButton m && m.Pressed && IsActive)
                Finish(confirmed: false);
        };

        GD.Print("[确认弹窗] 已加载");
    }

    /// <summary>Esc = 取消（键盘党友好）</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsActive)
            return;

        // 既认 ui_cancel 动作，也直接认 Esc 键本身：
        // 将来万一输入映射被改动（或测试用合成事件没带 action），取消这条依然灵。
        bool pressedEsc = @event.IsActionPressed("ui_cancel")
            || (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape);
        if (pressedEsc)
        {
            GetViewport().SetInputAsHandled();
            Finish(confirmed: false);
        }
    }

    /// <summary>
    /// 弹出确认框。
    /// </summary>
    /// <param name="titleKey">标题文案键</param>
    /// <param name="bodyKey">正文文案键</param>
    /// <param name="onConfirm">玩家点"确定"后要执行的事</param>
    /// <param name="onCancel">玩家取消后要执行的事（可省略）</param>
    /// <param name="okKey">确定按钮的文案键（默认用"返回主菜单"那句，调用方一般会自己传）</param>
    /// <param name="cancelKey">取消按钮的文案键</param>
    public static void Ask(string titleKey, string bodyKey, Action onConfirm, Action onCancel = null,
        string okKey = "confirm.back_yes", string cancelKey = "confirm.back_no")
    {
        var host = Instance;
        if (host == null)
        {
            // 弹窗没加载（理论上不会发生）：直接执行，别把流程卡死
            onConfirm?.Invoke();
            return;
        }
        host.ShowAsk(titleKey, bodyKey, onConfirm, onCancel, okKey, cancelKey);
    }

    private void ShowAsk(string titleKey, string bodyKey, Action confirm, Action cancel,
        string okKey, string cancelKey)
    {
        onConfirm = confirm;
        onCancel = cancel;

        titleLabel.Text = DataStore.Text(titleKey);
        bodyLabel.Text = DataStore.Text(bodyKey);
        okButton.Text = DataStore.Text(okKey);
        cancelButton.Text = DataStore.Text(cancelKey);

        // 弹窗期间：对话系统让位（否则点击会被"推进对话"吃掉）
        DialogueManager.Instance?.SetUiSuppressed(true);

        root.Visible = true;
        root.Modulate = new Color(1, 1, 1, 0);

        var tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 1f, 0.16);
    }

    private void Finish(bool confirmed)
    {
        if (!IsActive)
            return;

        root.Visible = false;
        DialogueManager.Instance?.SetUiSuppressed(false);

        var ok = onConfirm;
        var no = onCancel;
        onConfirm = null;
        onCancel = null;

        AudioManager.Instance?.PlaySfx(confirmed ? AudioManager.SfxConfirm : AudioManager.SfxClick, -10f);
        if (confirmed) ok?.Invoke();
        else no?.Invoke();
    }

    // ==================== 小样式 ====================

    /// <summary>面板样式：和选项按钮同一套"奶油纸片"</summary>
    private static StyleBoxFlat MakePaper()
    {
        var sb = new StyleBoxFlat { BgColor = new Color(0.95f, 0.90f, 0.80f, 0.98f) };
        sb.SetCornerRadiusAll(20);
        sb.CornerDetail = 20;
        sb.BorderWidthLeft = sb.BorderWidthTop = sb.BorderWidthRight = sb.BorderWidthBottom = 1;
        sb.BorderColor = new Color(1f, 0.96f, 0.88f, 0.6f);
        sb.ShadowColor = new Color(0.15f, 0.08f, 0.03f, 0.45f);
        sb.ShadowSize = 16;
        sb.ShadowOffset = new Vector2(0, 6);
        return sb;
    }

    private static Button MakeButton(string textKey)
    {
        var btn = new Button
        {
            Text = DataStore.Text(textKey),
            CustomMinimumSize = new Vector2(300, 62),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 24);
        btn.AddThemeColorOverride("font_color", new Color(0.33f, 0.2f, 0.11f));
        btn.AddThemeColorOverride("font_hover_color", new Color(0.24f, 0.13f, 0.06f));
        btn.AddThemeStyleboxOverride("normal", MakeButtonPaper(new Color(0.945f, 0.855f, 0.718f), 6));
        btn.AddThemeStyleboxOverride("hover", MakeButtonPaper(new Color(1f, 0.913f, 0.788f), 11));
        btn.AddThemeStyleboxOverride("pressed", MakeButtonPaper(new Color(0.82f, 0.66f, 0.47f), 3));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        UiSounds.Wire(btn);
        return btn;
    }

    private static StyleBoxFlat MakeButtonPaper(Color bg, int shadow)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(16);
        sb.CornerDetail = 16;
        sb.ShadowColor = new Color(0.22f, 0.12f, 0.05f, 0.35f);
        sb.ShadowSize = shadow;
        sb.ShadowOffset = new Vector2(0, 3);
        sb.ContentMarginLeft = sb.ContentMarginRight = 28;
        sb.ContentMarginTop = sb.ContentMarginBottom = 14;
        return sb;
    }
}
