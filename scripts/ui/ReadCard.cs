using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 内心解读 UI（第十八轮·第十一章《置顶》招牌机制）
///
/// 【这是什么】
/// 她说了一句模糊的话——比如深夜的「没事。」——同一句话，读法不同，回法不同，
/// 走出的路也不同。屏幕上先浮出她的原话气泡，下方推出三张"解读纸片"：
/// 玩家选一种读法，主角就按那种读法回复。
///
/// 【和心声纸片的区别】
/// 心声（HeartChips）是"你心里正在想的那句"，不记数值；
/// 解读（ReadCard）走主线选择——记好感/勇气、进选择回顾、真的改变他说出口的话。
/// 所以体量更大：有她的原话气泡压阵，每张纸片带一行"回什么"的小字。
///
/// 【实现说明】
/// 视觉完全复用心声纸片那套奶油纸质感（ChoicePanel.MakeStyle 同源），
/// 点击判定走 HasCardAt()（外扩 8px，和 ChoicePanel/HeartChips 同一坐标习惯），
/// DialogueManager 在"选择阶段"据此放行点击。
/// </summary>
public partial class ReadCard : Control
{
    private PanelContainer quoteBubble;
    private Label quoteLabel;
    private HBoxContainer box;
    private readonly List<Button> cards = new();
    private Action<int> callback;

    // 和 ChoicePanel / HeartChips 同一套文字三态颜色
    private static readonly Color TextNormal = new(0.33f, 0.2f, 0.11f);
    private static readonly Color TextHover = new(0.24f, 0.13f, 0.06f);
    private static readonly Color TextPressed = new(0.99f, 0.96f, 0.9f);
    private static readonly Color TextSub = new(0.52f, 0.42f, 0.3f);
    private static readonly Color TextSubHover = new(0.42f, 0.32f, 0.2f);

    public override void _Ready()
    {
        // 底部锚定，坐在对话框上沿之上。比心声横带高一层——要多容一行她的原话。
        // （C# 的 AnchorXxx setter 默认 keep_offset = true，四个偏移必须全部显式写死，
        //   不然整条带宽度归零、纸片全贴屏幕左边——HeartChips 踩过的坑，这里直接绕开。）
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetRight = 0f;
        // 带高 304：原话气泡（约 60）+ 间隔 + 卡片（约 150，见下）都得住得下，
        // 不然 column 虽然居中、内容也会顶出带子压到顶部提示。
        OffsetTop = -680;
        OffsetBottom = -376;
        MouseFilter = MouseFilterEnum.Ignore;

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        column.AddThemeConstantOverride("separation", 18);
        AddChild(column);

        // ---- 她的原话气泡（微信里收到的样子：白底、细边） ----
        quoteBubble = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var bubbleStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.995f, 0.985f, 0.955f, 0.96f),
            BorderColor = new Color(0.85f, 0.75f, 0.6f, 0.8f),
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18,
            ContentMarginLeft = 34,
            ContentMarginRight = 34,
            ContentMarginTop = 14,
            ContentMarginBottom = 14,
        };
        quoteBubble.AddThemeStyleboxOverride("panel", bubbleStyle);
        quoteBubble.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        column.AddChild(quoteBubble);

        quoteLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        quoteLabel.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        quoteLabel.AddThemeFontSizeOverride("font_size", 32);
        quoteLabel.AddThemeColorOverride("font_color", new Color(0.22f, 0.14f, 0.1f));
        quoteBubble.AddChild(quoteLabel);

        // ---- 三张解读纸片 ----
        box = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        box.AddThemeConstantOverride("separation", 24);
        column.AddChild(box);

        Hide();
    }

    /// <summary>浮出她的原话 + 三张解读纸片（options 一般是 3 个，带 sub 副行）</summary>
    public void ShowCards(string quote, List<ChoiceOptionData> options, Action<int> onSelected)
    {
        callback = onSelected;

        foreach (var c in cards)
            c.QueueFree();
        cards.Clear();

        quoteLabel.Text = $"「{quote}」";
        quoteBubble.Modulate = new Color(1, 1, 1, 0);

        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            // 高度 150 是按现有读法文案算的：主行 22 字 @25px 在卡内 356px 宽里折两行（约 70），
            // 副行 15 字 @21px 一行（约 30），加间隔 6、样式盒上下边距 28 → 约 134，留一档余量。
            // （Button 的最小尺寸不含子控件——cell 不给足高度的话整张卡会塌成细条。）
            var btn = new Button
            {
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(400, 150),
            };
            btn.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());

            // 关键（2026-10-11 回归踩坑）：Button 不是容器，子控件不会被自动撑满——
            // cell 锚点必须铺满按钮矩形（扣掉样式盒四边留白），否则尺寸为 0，
            // 标签在零宽度里 autowrap 成"一字一行"竖着溢出屏幕。
            var cell = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorRight = 1f,
                AnchorBottom = 1f,
                OffsetLeft = 22,
                OffsetTop = 14,
                OffsetRight = -22,
                OffsetBottom = -14,
            };
            cell.AddThemeConstantOverride("separation", 6);
            btn.AddChild(cell);

            var main = new Label
            {
                Text = opt.Text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            main.AddThemeFontSizeOverride("font_size", 25);
            main.AddThemeColorOverride("font_color", TextNormal);
            cell.AddChild(main);

            if (!string.IsNullOrEmpty(opt.Sub))
            {
                var sub = new Label
                {
                    Text = opt.Sub,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                sub.AddThemeFontSizeOverride("font_size", 21);
                sub.AddThemeColorOverride("font_color", TextSub);
                cell.AddChild(sub);
            }

            // 和主选项同一套"奶油纸片"，尺寸介于心声纸片和主选项之间
            var normal = ChoicePanel.MakeStyle(new Color(0.945f, 0.855f, 0.718f), 6, new Vector2(0, 3));
            normal.ContentMarginLeft = 22;
            normal.ContentMarginRight = 22;
            normal.ContentMarginTop = 14;
            normal.ContentMarginBottom = 14;
            var hover = ChoicePanel.MakeStyle(new Color(1f, 0.913f, 0.788f), 12, new Vector2(0, 4));
            hover.ContentMarginLeft = 22;
            hover.ContentMarginRight = 22;
            hover.ContentMarginTop = 14;
            hover.ContentMarginBottom = 14;
            var pressed = ChoicePanel.MakeStyle(new Color(0.82f, 0.66f, 0.47f), 4, new Vector2(0, 2));
            pressed.ContentMarginLeft = 22;
            pressed.ContentMarginRight = 22;
            pressed.ContentMarginTop = 14;
            pressed.ContentMarginBottom = 14;
            btn.AddThemeStyleboxOverride("normal", normal);
            btn.AddThemeStyleboxOverride("hover", hover);
            btn.AddThemeStyleboxOverride("pressed", pressed);
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            int index = i;
            btn.Pressed += () => OnCardPressed(index);

            btn.MouseEntered += () =>
            {
                AudioManager.Instance?.PlaySfx(AudioManager.SfxHover, -16f);
                TweenScale(btn, 1.03f, 0.12f);
            };
            btn.MouseExited += () => TweenScale(btn, 1.0f, 0.12f);

            box.AddChild(btn);
            cards.Add(btn);

            // 气泡先落，纸片一张一张跟着冒（先听见她的话，再听见自己的读法）
            btn.Modulate = new Color(1, 1, 1, 0);
            var tween = btn.CreateTween();
            tween.TweenInterval(0.25f + i * 0.1f);
            tween.TweenProperty(btn, "modulate:a", 1.0f, 0.22f);
        }

        Show();

        var bubbleTween = quoteBubble.CreateTween();
        bubbleTween.TweenProperty(quoteBubble, "modulate:a", 1.0f, 0.3f);
    }

    /// <summary>收起全部</summary>
    public void HideAll()
    {
        callback = null;
        foreach (var c in cards)
            c.QueueFree();
        cards.Clear();
        Hide();
    }

    /// <summary>
    /// 这个点是不是落在某张解读纸片上？（对话管理器在"选择阶段"据此放行点击）
    /// 判定习惯与 ChoicePanel.HasButtonAt / HeartChips.HasChipAt 完全一致：外扩 8px。
    /// </summary>
    public bool HasCardAt(Vector2 globalPos)
    {
        foreach (var btn in cards)
        {
            if (!GodotObject.IsInstanceValid(btn) || !btn.IsVisibleInTree())
                continue;
            if (btn.GetGlobalRect().Grow(8).HasPoint(globalPos))
                return true;
        }
        return false;
    }

    private void OnCardPressed(int index)
    {
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -8f);

        var cb = callback;
        callback = null;
        // 点完即收（和 ChoicePanel 一个规矩）：三张"读法"不能压在回复对白上不走
        HideAll();
        cb?.Invoke(index);
    }

    /// <summary>缩放小动效（先杀掉上一次的；读 meta 前先问 HasMeta，避免 ERROR 日志）</summary>
    private static void TweenScale(Button btn, float scale, float dur)
    {
        if (!GodotObject.IsInstanceValid(btn))
            return;
        if (btn.HasMeta("read_tw"))
            btn.GetMeta("read_tw").As<Tween>()?.Kill();
        btn.PivotOffset = btn.Size / 2f;
        var tween = btn.CreateTween();
        tween.TweenProperty(btn, "scale", Vector2.One * scale, dur)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        btn.SetMeta("read_tw", tween);
    }
}
