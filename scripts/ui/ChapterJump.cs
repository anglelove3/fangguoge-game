using Godot;

/// <summary>
/// 章节直达（第三轮 F8：主菜单与通关结局页共用）
/// 章表浮层 + 章节场景路径解析。点哪章进哪章——演示、重玩都靠它。
/// </summary>
public static class ChapterJump
{
    /// <summary>目前开发到的最后一章</summary>
    public const int LastChapter = 16;

    public static readonly string[] ChapterNames =
    {
        "序章 · 那首歌",
        "第一章 · 寄居蟹",
        "第二章 · 一千公里",
        "第三章 · 天平",
        "第四章 · 星月",
        "第五章 · 晚安",
        "第六章 · 手滑",
        "第七章 · 碧蓝蓝天",
        "第八章 · 放过自己",
        "第九章 · 三点水",
        "第十章 · 还想再见",
        "第十一章 · 置顶",
        "第十二章 · 向南",
        "第十三章 · 十里长亭",
        "第十四章 · 爱就梭哈",
        "第十五章 · 那束光",
        "第十六章 · 今天天气晴",
    };

    /// <summary>章号 → 场景路径（0 = 序章）</summary>
    public static string ScenePath(int chapter) => chapter switch
    {
        0 => "res://scenes/chapters/prologue/prologue.tscn",
        _ => $"res://scenes/chapters/ch{chapter:00}/ch{chapter:00}.tscn",
    };

    /// <summary>在 host 上弹章节选择面板，点选即跳章</summary>
    public static void ShowPanel(Control host) => ShowPanel(host, LastChapter);

    /// <summary>只列出 0~maxChapter（第三轮：继续游戏里只给已玩到的章）</summary>
    public static void ShowPanel(Control host, int maxChapter)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(layer);

        layer.Draw += () => layer.DrawRect(layer.GetRect(), new Color(0.04f, 0.05f, 0.08f, 0.72f));
        layer.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed)
                layer.QueueFree();
        };

        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.11f, 0.14f, 0.96f),
            BorderColor = new Color(0.90f, 0.80f, 0.55f, 0.5f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        };
        style.SetCornerRadiusAll(14);
        style.ContentMarginLeft = 22; style.ContentMarginRight = 22;
        style.ContentMarginTop = 18; style.ContentMarginBottom = 18;
        panel.AddThemeStyleboxOverride("panel", style);
        layer.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -215; panel.OffsetRight = 215;
        panel.OffsetTop = -300; panel.OffsetBottom = 300;

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 4);
        panel.AddChild(col);
        // 【第三轮反馈 8】必须撑满面板：以前这里没设锚点，VBox 取的是"最小尺寸"，
        // 而里面唯一的 ScrollContainer 是 ExpandFill——父节点没有高度 → 列表高度 0，
        // 面板点开只剩标题和一片空白，一章也点不到。
        col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var title = new Label
        {
            Text = "选择章节",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 0.85f));
        col.AddChild(title);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore });

        var scroll = new ScrollContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        col.AddChild(scroll);

        var list = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);

        int showMax = Mathf.Clamp(maxChapter, 0, LastChapter);
        for (int i = 0; i <= showMax; i++)
        {
            int chapter = i;
            var btn = new Button
            {
                Text = i < ChapterNames.Length ? ChapterNames[i] : $"第 {i} 章",
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            btn.AddThemeFontSizeOverride("font_size", 21);
            btn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.04f) });
            btn.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color(0.90f, 0.80f, 0.55f, 0.16f) });
            btn.Pressed += () =>
            {
                layer.QueueFree();
                GameManager.Instance.ChangeSceneWithTransition(ScenePath(chapter));
            };
            list.AddChild(btn);
        }
    }
}
