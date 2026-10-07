using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 选项面板 UI
///
/// 【知识点 - 动态创建节点】
/// 选项数量不固定（有的选择 2 个选项，有的 3 个），
/// 所以不能用固定按钮，而是在代码里 new 出按钮再 AddChild 到容器里。
/// 这就是"动态生成 UI"，做选择系统、背包系统都靠它。
///
/// 【视觉说明 - "奶油纸片"】
/// 2026-10-04 试玩反馈：原来的橙色扁按钮和画风不搭、字色也突兀。
/// 现在整套和主菜单按钮统一：
///   - 暖米色底 + 深褐字（纸张感），圆角 + 描边 + 柔和投影
///   - 楷体 + 字间距，悬停变亮、投影变大（像纸片被抬起来一点）
///   - 面板背后一层很轻的压暗（choice_panel.tscn 里的 Scrim），
///     让选项从背景里"浮"出来，不再和画面打架
/// </summary>
public partial class ChoicePanel : Control
{
    private VBoxContainer optionsBox;
    private readonly List<Button> buttons = new();
    private Action<int> callback;

    // 选项文字字体（楷体 + 字间距）。整个面板共享一份，不用反复创建。
    private static Font choiceFont;

    // 文字三态颜色（和主菜单按钮一致）
    private static readonly Color TextNormal = new(0.33f, 0.2f, 0.11f);
    private static readonly Color TextHover = new(0.24f, 0.13f, 0.06f);
    private static readonly Color TextPressed = new(0.99f, 0.96f, 0.9f);

    public override void _Ready()
    {
        optionsBox = GetNode<VBoxContainer>("CenterContainer/OptionsBox");
        Hide(); // 默认隐藏
    }

    /// <summary>
    /// 显示一组选项
    /// </summary>
    public void ShowChoices(List<ChoiceOptionData> options, Action<int> onSelected)
    {
        callback = onSelected;

        // 清掉上一组按钮
        foreach (var b in buttons)
        {
            b.QueueFree();
        }
        buttons.Clear();

        // 动态生成新按钮
        for (int i = 0; i < options.Count; i++)
        {
            var btn = new Button();
            btn.Text = options[i].Text;
            btn.CustomMinimumSize = new Vector2(780, 78);
            btn.AddThemeFontOverride("font", GetChoiceFont());
            btn.AddThemeFontSizeOverride("font_size", 30);

            // 文字颜色：平时暖褐、悬停更深、按下变米白（底色会变深）
            btn.AddThemeColorOverride("font_color", TextNormal);
            btn.AddThemeColorOverride("font_hover_color", TextHover);
            btn.AddThemeColorOverride("font_pressed_color", TextPressed);

            // 和主菜单按钮同一套"奶油纸片"质感
            btn.AddThemeStyleboxOverride("normal", MakeStyle(new Color(0.945f, 0.855f, 0.718f), 6, new Vector2(0, 3)));
            btn.AddThemeStyleboxOverride("hover", MakeStyle(new Color(1f, 0.913f, 0.788f), 12, new Vector2(0, 4)));
            btn.AddThemeStyleboxOverride("pressed", MakeStyle(new Color(0.82f, 0.66f, 0.47f), 4, new Vector2(0, 2)));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); // 不要键盘焦点虚线框

            int index = i; // 记下编号（不能直接用 i，会被后面的循环改掉）
            btn.Pressed += () => OnOptionPressed(index);

            // 悬停：轻轻"嗒"一声 + 整张纸片抬起来一点（和主菜单按钮同款手感）
            btn.MouseEntered += () =>
            {
                AudioManager.Instance?.PlaySfx(AudioManager.SfxHover, -16f);
                TweenScale(btn, 1.03f, 0.12f);
            };
            btn.MouseExited += () => TweenScale(btn, 1.0f, 0.12f);

            optionsBox.AddChild(btn);
            buttons.Add(btn);

            // 小演出：选项按钮一个一个错开淡入，像发牌一样
            btn.Modulate = new Color(1, 1, 1, 0);
            var tween = CreateTween();
            tween.TweenInterval(i * 0.07f);
            tween.TweenProperty(btn, "modulate:a", 1.0f, 0.15f);
        }

        Show();
    }

    /// <summary>
    /// 这个点时是不是落在某个"还在显示"的选项按钮上？（对话管理器用它决定要不要放行点击）
    /// 判定沿用 DialogueManager.FindPriorityControl 的同一套坐标习惯：外扩 8px、更宽容。
    /// </summary>
    public bool HasButtonAt(Vector2 globalPos)
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

    private void OnOptionPressed(int index)
    {
        // 确定音：比普通点击更"扎实"，给玩家"这一下算数"的感觉
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirm, -8f);

        Hide();

        var cb = callback;
        callback = null;
        cb?.Invoke(index);
    }

    /// <summary>缩放小动效（先杀掉上一次的，避免悬停进出互相打架）</summary>
    private static void TweenScale(Button btn, float scale, float dur)
    {
        if (!GodotObject.IsInstanceValid(btn))
            return;
        // 读 meta 前必须先问一句"有没有"：GetMeta 在键不存在时会打 ERROR+堆栈（第十二轮修掉的日志脏点）
        if (btn.HasMeta("juice_tw"))
            btn.GetMeta("juice_tw").As<Tween>()?.Kill();
        btn.PivotOffset = btn.Size / 2f; // 以中心为轴，看起来是"原地抬起"
        var tween = btn.CreateTween();
        tween.TweenProperty(btn, "scale", Vector2.One * scale, dur)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        btn.SetMeta("juice_tw", tween);
    }

    /// <summary>选项字体：楷体 + 字间距 6（和主菜单按钮同一款感觉），只创建一次。心声小纸片也复用它。</summary>
    internal static Font GetChoiceFont()
    {
        if (choiceFont == null)
        {
            var variation = new FontVariation();
            variation.BaseFont = GD.Load<FontFile>("res://assets/fonts/LXGWWenKai-Regular.ttf");
            variation.SpacingGlyph = 6; // 字与字之间留一点呼吸感
            choiceFont = variation;
        }
        return choiceFont;
    }

    /// <summary>
    /// 创建"奶油纸片"样式：暖色底 + 浅色描边 + 柔和投影（心声小纸片也复用它）
    /// </summary>
    internal static StyleBoxFlat MakeStyle(Color bg, int shadowSize, Vector2 shadowOffset)
    {
        var style = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16,
            CornerRadiusBottomRight = 16,
            CornerDetail = 16,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 0.945f, 0.85f, 0.5f),
            ShadowColor = new Color(0.22f, 0.12f, 0.05f, 0.35f),
            ShadowSize = shadowSize,
            ShadowOffset = shadowOffset,
            ContentMarginLeft = 32,
            ContentMarginRight = 32,
            ContentMarginTop = 15,
            ContentMarginBottom = 15
        };
        return style;
    }
}
