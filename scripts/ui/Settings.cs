using Godot;
using System;

/// <summary>
/// 设置界面（第十一轮重做）
///
/// 【原来什么样 / 为什么改】
/// 旧版只有一个"全屏"勾选框，而且是本地临时生效——关掉游戏就忘（试玩反馈 C3）。
/// 现在四项都能调、并且会存进 user://settings.cfg，下次开机自动恢复：
///   音乐音量 / 音效音量 / 文字速度 / 自动播放速度 + 全屏
///
/// 【知识点 - 代码搭 UI】
/// 整个界面都是 new 出来再 AddChild 挂上去的，场景文件里只留一个空壳。
/// 好处：改布局不用在编辑器里拖，看代码就知道长什么样；
/// 这套写法在本项目里已经用过很多次（ChatOverlay、ConfirmHost 都是这么搭的）。
///
/// 【文案在哪】
/// 所有中文字都来自 data/text/ui_text.json 的 settings.* 键，改字不用碰代码。
/// </summary>
public partial class Settings : Control
{
    private const float CardWidth = 900f;

    // 纸片上的字色（和选项面板同一套暖褐色）
    private static readonly Color InkNormal = new(0.3f, 0.2f, 0.12f);
    private static readonly Color InkFaint = new(0.45f, 0.34f, 0.22f, 0.85f);

    public override void _Ready()
    {
        BuildBackground();

        // ---- 居中的"纸片"卡片 ----
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", MakePaper());
        card.CustomMinimumSize = new Vector2(CardWidth, 0);
        center.AddChild(card);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 56);
        margin.AddThemeConstantOverride("margin_right", 56);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 36);
        card.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 22);
        margin.AddChild(box);

        // ---- 标题 ----
        var title = new Label
        {
            Text = DataStore.Text("settings.title"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 44);
        title.AddThemeColorOverride("font_color", InkNormal);
        box.AddChild(title);

        box.AddChild(MakeLine());

        // ---- 四根滑条 ----
        var gs = GameSettings.Instance;
        AddSliderRow(box, DataStore.Text("settings.music"), gs.MusicVolume, value =>
        {
            gs.MusicVolume = value;
            AudioManager.Instance?.RefreshVolumes();
        });
        AddSliderRow(box, DataStore.Text("settings.sfx"), gs.SfxVolume, value =>
        {
            gs.SfxVolume = value;
        });
        AddSliderRow(box, DataStore.Text("settings.text_speed"), gs.TextSpeed, value =>
        {
            gs.TextSpeed = value;
        });
        AddSliderRow(box, DataStore.Text("settings.auto_speed"), gs.AutoSpeed, value =>
        {
            gs.AutoSpeed = value;
        });

        // ---- 全屏开关 ----
        var fullscreen = new CheckButton
        {
            Text = DataStore.Text("settings.fullscreen"),
            ButtonPressed = gs.Fullscreen,
        };
        fullscreen.AddThemeFontSizeOverride("font_size", 26);
        fullscreen.AddThemeColorOverride("font_color", InkNormal);
        fullscreen.Toggled += pressed =>
        {
            gs.Fullscreen = pressed;
            gs.ApplyWindowMode();
            gs.Save();
        };
        box.AddChild(fullscreen);

        // ---- 小提示 + 返回 ----
        var tip = new Label
        {
            Text = DataStore.Text("settings.tip"),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        tip.AddThemeFontSizeOverride("font_size", 18);
        tip.AddThemeColorOverride("font_color", InkFaint);
        box.AddChild(tip);

        var back = new Button
        {
            Text = DataStore.Text("settings.back"),
            CustomMinimumSize = new Vector2(320, 58),
        };
        back.AddThemeFontSizeOverride("font_size", 24);
        back.AddThemeColorOverride("font_color", InkNormal);
        back.AddThemeColorOverride("font_hover_color", new Color(0.2f, 0.12f, 0.05f));
        back.AddThemeStyleboxOverride("normal", MakeButtonPaper(new Color(0.945f, 0.855f, 0.718f), 6));
        back.AddThemeStyleboxOverride("hover", MakeButtonPaper(new Color(1f, 0.913f, 0.788f), 11));
        back.AddThemeStyleboxOverride("pressed", MakeButtonPaper(new Color(0.82f, 0.66f, 0.47f), 3));
        back.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        back.Pressed += OnBackPressed;
        box.AddChild(back);

        // 滑条的落盘挂在各自的 DragEnded 上（见 AddSliderRow），这里不用统一处理

        UiSounds.WireAll(this);
        GD.Print("[设置界面] 已加载");
    }

    // ==================== 一行滑条 ====================

    /// <summary>
    /// 加一行"名称 ——— 滑条 ——— 数值"，改动立刻写进 GameSettings 并存档。
    /// </summary>
    private void AddSliderRow(VBoxContainer parent, string label, float startValue, Action<float> apply)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 22);
        parent.AddChild(row);

        var name = new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(200, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.AddThemeFontSizeOverride("font_size", 24);
        name.AddThemeColorOverride("font_color", InkNormal);
        row.AddChild(name);

        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            Value = startValue,
            CustomMinimumSize = new Vector2(440, 40),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(slider);

        var value = new Label
        {
            Text = Percent(startValue),
            CustomMinimumSize = new Vector2(96, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.AddThemeFontSizeOverride("font_size", 22);
        value.AddThemeColorOverride("font_color", InkFaint);
        row.AddChild(value);

        // 拖动过程只更新显示和内存里的值，落盘放到松手之后（ValueChanged 一路都在触发，别每次都写文件）
        slider.DragEnded += _ => GameSettings.Instance?.Save();
        slider.ValueChanged += newValue =>
        {
            apply((float)newValue);
            value.Text = Percent((float)newValue);
        };
    }

    private static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

    // ==================== 小工具 ====================

    private void BuildBackground()
    {
        var bg = new ColorRect { Color = new Color(0.09f, 0.075f, 0.11f) };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        // 左上角一点点暖光，免得整屏死黑
        var glow = new ColorRect
        {
            Color = new Color(0.98f, 0.86f, 0.66f, 0.05f),
            OffsetLeft = -200,
            OffsetTop = -260,
            OffsetRight = 900,
            OffsetBottom = 520,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(glow);
    }

    /// <summary>标题下面那条细细的分隔线</summary>
    private static Control MakeLine()
    {
        var line = new ColorRect
        {
            Color = new Color(0.3f, 0.2f, 0.12f, 0.18f),
            CustomMinimumSize = new Vector2(0, 2),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return line;
    }

    private static StyleBoxFlat MakePaper()
    {
        var sb = new StyleBoxFlat { BgColor = new Color(0.95f, 0.91f, 0.82f, 0.97f) };
        sb.SetCornerRadiusAll(22);
        sb.CornerDetail = 22;
        sb.BorderWidthLeft = sb.BorderWidthTop = sb.BorderWidthRight = sb.BorderWidthBottom = 1;
        sb.BorderColor = new Color(1f, 0.96f, 0.88f, 0.5f);
        sb.ShadowColor = new Color(0.1f, 0.06f, 0.02f, 0.5f);
        sb.ShadowSize = 20;
        sb.ShadowOffset = new Vector2(0, 8);
        return sb;
    }

    private static StyleBoxFlat MakeButtonPaper(Color bg, int shadow)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(16);
        sb.CornerDetail = 16;
        sb.ShadowColor = new Color(0.22f, 0.12f, 0.05f, 0.35f);
        sb.ShadowSize = shadow;
        sb.ShadowOffset = new Vector2(0, 3);
        sb.ContentMarginLeft = sb.ContentMarginRight = 30;
        sb.ContentMarginTop = sb.ContentMarginBottom = 14;
        return sb;
    }

    /// <summary>返回主菜单（改动已经实时存进 settings.cfg，不用再点"保存"）</summary>
    private void OnBackPressed()
    {
        GameSettings.Instance?.Save();
        GameManager.Instance.ChangeSceneWithTransition("res://scenes/main_menu/main_menu.tscn");
    }
}
