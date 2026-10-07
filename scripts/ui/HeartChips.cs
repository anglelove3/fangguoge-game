using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 心声微选择 UI（第十二轮）
///
/// 【这是什么】
/// 剧情走着走着，偶尔会停一下，把"你心里其实正在想的那一句"递到玩家手里：
/// 对话框上方浮出两三张很小的"奶油纸片"，选哪张都不影响好感/勇气——
/// 它不判定结局，只是让玩家把自己的心意"放"进故事里一次。
///
/// 【设计要点】
///   - 位置比选项面板低（贴着对话框上沿），体量明显更小，
///     一眼就能和"要算数的大选择"区分开；
///   - 选择只记录（结束页回顾会展示）+ 播放分支对白；
///   - 点击判定走 HasChipAt()，和选项面板同一套坐标习惯，
///     这样 DialogueManager 在"选择阶段"能放行落在纸片上的点击。
/// </summary>
public partial class HeartChips : Control
{
    private HBoxContainer box;
    private readonly List<Button> buttons = new();
    private Action<int> callback;
    private Tween fadeTween;

    // 和 ChoicePanel 同款文字三态颜色（同一套视觉语言）
    private static readonly Color TextNormal = new(0.33f, 0.2f, 0.11f);
    private static readonly Color TextHover = new(0.24f, 0.13f, 0.06f);
    private static readonly Color TextPressed = new(0.99f, 0.96f, 0.9f);

    public override void _Ready()
    {
        // 底部锚定：坐在对话框上沿之上（对话框顶边在 -310，这里收在 -436..-376）
        SetAnchorsPreset(LayoutPreset.BottomWide);
        AnchorLeft = 0f;
        AnchorRight = 1f;
        AnchorTop = 1f;
        AnchorBottom = 1f;
        // 注意（2026-10-07 回归踩坑）：C# 的 AnchorXxx 属性 setter 默认 keep_offset = true，
        // 会把 OffsetXxx 改成"保持当前像素位置"的值再算锚点。_Ready 里节点还没布局过、
        // 停在 0×0，于是 OffsetRight 被算成 -全屏宽，整条横带宽度归零 → 纸片全贴屏幕左边。
        // 所以四个偏移必须全部显式写死，不能只写上下两个。
        OffsetLeft = 0f;
        OffsetRight = 0f;
        OffsetTop = -436;
        OffsetBottom = -376;
        MouseFilter = MouseFilterEnum.Ignore;

        // 整条横带里居中排纸片。不要用 CenterContainer 包一层：
        // 纯代码 new 的容器没有场景文件里的初始尺寸，居中依赖测量时机，容易再踩坑。
        box = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        box.AddThemeConstantOverride("separation", 26);
        AddChild(box);

        Hide();
    }

    /// <summary>浮出几张小纸片（options 一般是 2~3 个）</summary>
    public void ShowChoices(List<HeartOptionData> options, Action<int> onSelected)
    {
        callback = onSelected;
        fadeTween?.Kill();

        foreach (var b in buttons)
            b.QueueFree();
        buttons.Clear();

        for (int i = 0; i < options.Count; i++)
        {
            var btn = new Button
            {
                Text = options[i].Text,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 56),
            };
            btn.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
            btn.AddThemeFontSizeOverride("font_size", 26);
            btn.AddThemeColorOverride("font_color", TextNormal);
            btn.AddThemeColorOverride("font_hover_color", TextHover);
            btn.AddThemeColorOverride("font_pressed_color", TextPressed);

            // 和主菜单/选项同一套"奶油纸片"，但更薄更小
            var normal = ChoicePanel.MakeStyle(new Color(0.945f, 0.855f, 0.718f), 5, new Vector2(0, 2));
            normal.ContentMarginTop = 8;
            normal.ContentMarginBottom = 8;
            var hover = ChoicePanel.MakeStyle(new Color(1f, 0.913f, 0.788f), 10, new Vector2(0, 3));
            hover.ContentMarginTop = 8;
            hover.ContentMarginBottom = 8;
            var pressed = ChoicePanel.MakeStyle(new Color(0.82f, 0.66f, 0.47f), 3, new Vector2(0, 1));
            pressed.ContentMarginTop = 8;
            pressed.ContentMarginBottom = 8;
            btn.AddThemeStyleboxOverride("normal", normal);
            btn.AddThemeStyleboxOverride("hover", hover);
            btn.AddThemeStyleboxOverride("pressed", pressed);
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            int index = i;
            btn.Pressed += () => OnChipPressed(index);

            btn.MouseEntered += () =>
            {
                AudioManager.Instance?.PlaySfx(AudioManager.SfxHover, -16f);
                TweenScale(btn, 1.04f, 0.12f);
            };
            btn.MouseExited += () => TweenScale(btn, 1.0f, 0.12f);

            box.AddChild(btn);
            buttons.Add(btn);

            // 一张一张轻轻冒出来（错开一点，像心里的话先说出口的那半句）
            btn.Modulate = new Color(1, 1, 1, 0);
            var tween = btn.CreateTween();
            tween.TweenInterval(i * 0.08f);
            tween.TweenProperty(btn, "modulate:a", 1.0f, 0.18f);
        }

        Show();
    }

    /// <summary>收起所有小纸片</summary>
    public void HideAll()
    {
        callback = null;
        fadeTween?.Kill();
        foreach (var b in buttons)
            b.QueueFree();
        buttons.Clear();
        Hide();
    }

    /// <summary>
    /// 这个点时是不是落在某张小纸片上？（对话管理器在"选择阶段"用它决定要不要放行点击）
    /// 判定习惯和 ChoicePanel.HasButtonAt 完全一致：外扩 8px。
    /// </summary>
    public bool HasChipAt(Vector2 globalPos)
    {
        foreach (var btn in buttons)
        {
            if (!GodotObject.IsInstanceValid(btn) || !btn.IsVisibleInTree())
                continue;
            if (btn.GetGlobalRect().Grow(8).HasPoint(globalPos))
                return true;
        }
        return false;
    }

    private void OnChipPressed(int index)
    {
        // 轻轻一声"嗒"就够了——这是心里的声音，不是落锤
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -10f);

        var cb = callback;
        callback = null;
        cb?.Invoke(index);
    }

    /// <summary>缩放小动效（先杀掉上一次的；读 meta 前先问 HasMeta，避免 ERROR 日志）</summary>
    private static void TweenScale(Button btn, float scale, float dur)
    {
        if (!GodotObject.IsInstanceValid(btn))
            return;
        if (btn.HasMeta("heart_tw"))
            btn.GetMeta("heart_tw").As<Tween>()?.Kill();
        btn.PivotOffset = btn.Size / 2f;
        var tween = btn.CreateTween();
        tween.TweenProperty(btn, "scale", Vector2.One * scale, dur)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        btn.SetMeta("heart_tw", tween);
    }
}
