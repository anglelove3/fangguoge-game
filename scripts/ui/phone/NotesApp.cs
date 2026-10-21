using Godot;
using System.Collections.Generic;

/// <summary>
/// 备忘录（第十六章新增的 App，data/notes/notes.json 驱动）
///
/// 【这一章的道具，也是结局章的伏笔】
/// 台风天他在实验室写下「今天天气晴」。三种态由 ch16_final 的选项序号决定：
///   0 发给她   → 暖色脚注「已发送 · 她回：你们那里明明是台风。」
///   1 改成别的 → 灰色「已改成别的发出。这条留在草稿箱。」
///   2 删掉     → 灰色「草稿 · 未发送」
/// 第十七章（结局）在此基础上按结局追加回收文案——直接扩 json 的 states 即可，不动这个类。
///
/// 【为什么状态不写进 json】
/// json 是静态文案；玩家选了什么在存档里（choiceHistory）。用存档状态决定显示哪一态，
/// 换档、跳章、重玩都自然跟着变。
/// </summary>
public partial class NotesAppPage : PhoneAppPage
{
    // ==================== 数据 ====================

    public class NoteState
    {
        public string Footer { get; set; } = "";
        public string Tint { get; set; } = "";
        public string Extra { get; set; } = "";   // 结局章回收用（可选）
    }

    public class NoteEntry
    {
        public string Id { get; set; } = "";
        public int Chapter { get; set; }
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Body { get; set; } = new();
        public Dictionary<string, NoteState> States { get; set; } = new();
    }

    public class NotesFile
    {
        public List<NoteEntry> Notes { get; set; } = new();
    }

    private static NotesFile data;
    private static NotesFile Data =>
        data ??= DataStore.LoadJson<NotesFile>("res://data/notes/notes.json") ?? new NotesFile();

    // ==================== 配色 ====================

    private static readonly Color PaperBg = new(0.925f, 0.886f, 0.784f);   // 备忘录纸色
    private static readonly Color InkDark = new(0.16f, 0.13f, 0.08f);
    private static readonly Color InkGrey = new(0.42f, 0.40f, 0.36f);
    private static readonly Color DraftGrey = new(0.62f, 0.60f, 0.56f);

    // ==================== 节点 ====================

    private Control listView;
    private Control detailView;
    private Label detailDate;
    private Label detailTitle;
    private VBoxContainer detailBody;
    private Label detailFooter;

    private readonly List<NoteEntry> notes = new();
    private NoteEntry openNote;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var (_, box, _) = MakeHeader(PaperBg);
        AddBackAndTitle(box, PhoneData.App("notes")?.Name ?? "备忘录", InkDark);

        BuildListView();
        BuildDetailView();
    }

    /// <summary>每次打开都重读一遍（章节推进后可能有新内容、也可能换了态）</summary>
    public override void OnShown()
    {
        notes.Clear();
        notes.AddRange(Data.Notes);
        RebuildList();
        if (detailView != null)
            detailView.Visible = false;
    }

    public override bool GoBack()
    {
        if (detailView != null && detailView.Visible)
        {
            detailView.Visible = false;
            return true;
        }
        return false;
    }

    // ==================== 列表 ====================

    private void BuildListView()
    {
        listView = new Control { MouseFilter = MouseFilterEnum.Ignore };
        listView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(listView);

        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.OffsetTop = 116;
        pad.OffsetBottom = -18;
        pad.AddThemeConstantOverride("margin_left", 36);
        pad.AddThemeConstantOverride("margin_right", 36);
        listView.AddChild(pad);

        var scroll = new ScrollContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        pad.AddChild(scroll);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 14);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(col);
        listColumn = col;
    }

    private VBoxContainer listColumn;

    private void RebuildList()
    {
        if (listColumn == null)
            return;
        foreach (var child in listColumn.GetChildren())
            child.QueueFree();

        if (notes.Count == 0)
        {
            var empty = new Label
            {
                Text = "（没有备忘录）",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            empty.AddThemeFontSizeOverride("font_size", 24);
            empty.AddThemeColorOverride("font_color", InkGrey);
            listColumn.AddChild(empty);
            return;
        }

        foreach (var note in notes)
        {
            var card = MakeNoteCard(note);
            listColumn.AddChild(card);
        }
    }

    /// <summary>一条备忘录卡片：标题 + 日期 + 首行预览 + 状态角标</summary>
    private Button MakeNoteCard(NoteEntry note)
    {
        var state = StateOf(note);
        bool draft = state != null && state.Tint != "warm";

        var btn = new Button
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(0, 152),
        };
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.975f, 0.952f, 0.886f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 26,
            ContentMarginRight = 26,
            ContentMarginTop = 18,
            ContentMarginBottom = 16,
        };
        btn.AddThemeStyleboxOverride("normal", normal);
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.995f, 0.975f, 0.918f);
        btn.AddThemeStyleboxOverride("hover", hover);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(0.935f, 0.905f, 0.83f);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 26;
        col.OffsetRight = -26;
        col.OffsetTop = 16;
        col.AddThemeConstantOverride("separation", 6);
        btn.AddChild(col);

        var title = new Label
        {
            Text = note.Title,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", draft ? InkGrey : InkDark);
        col.AddChild(title);

        var date = new Label
        {
            Text = note.Date,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        date.AddThemeFontSizeOverride("font_size", 19);
        date.AddThemeColorOverride("font_color", InkGrey);
        col.AddChild(date);

        string preview = note.Body.Count > 0 ? note.Body[0] : "";
        var body = new Label
        {
            Text = draft ? Truncate(preview, 22) : preview,
            MouseFilter = MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        body.AddThemeFontSizeOverride("font_size", 20);
        body.AddThemeColorOverride("font_color", InkGrey);
        col.AddChild(body);

        if (state != null && !string.IsNullOrEmpty(state.Footer) && state.Tint != "warm")
        {
            var badge = new Label
            {
                Text = "草稿",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            badge.AddThemeFontSizeOverride("font_size", 17);
            badge.AddThemeColorOverride("font_color", DraftGrey);
            badge.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            badge.OffsetLeft = -80;
            badge.OffsetTop = 18;
            badge.OffsetRight = -16;
            btn.AddChild(badge);
        }

        btn.Pressed += () => OpenDetail(note);
        return btn;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

    // ==================== 详情 ====================

    private void BuildDetailView()
    {
        detailView = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        detailView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(detailView);

        var paper = new ColorRect
        {
            Color = new Color(0.968f, 0.945f, 0.876f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        paper.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        detailView.AddChild(paper);

        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.OffsetTop = 116;
        pad.OffsetBottom = -60;
        pad.AddThemeConstantOverride("margin_left", 46);
        pad.AddThemeConstantOverride("margin_right", 46);
        detailView.AddChild(pad);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 18);
        pad.AddChild(col);

        detailDate = new Label { MouseFilter = MouseFilterEnum.Ignore };
        detailDate.AddThemeFontSizeOverride("font_size", 19);
        detailDate.AddThemeColorOverride("font_color", InkGrey);
        col.AddChild(detailDate);

        detailTitle = new Label { MouseFilter = MouseFilterEnum.Ignore };
        detailTitle.AddThemeFontSizeOverride("font_size", 42);
        detailTitle.AddThemeColorOverride("font_color", InkDark);
        col.AddChild(detailTitle);

        var rule = new ColorRect
        {
            Color = new Color(0.72f, 0.68f, 0.58f, 0.45f),
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 1),
        };
        col.AddChild(rule);

        detailBody = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        detailBody.AddThemeConstantOverride("separation", 12);
        col.AddChild(detailBody);

        detailFooter = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        detailFooter.AddThemeFontSizeOverride("font_size", 18);
        detailFooter.AddThemeColorOverride("font_color", InkGrey);
        col.AddChild(detailFooter);

        var hint = new Label
        {
            Text = "（点任意处返回）",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 17);
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.52f, 0.46f, 0.7f));
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        hint.OffsetTop = -42;
        hint.OffsetBottom = -12;
        detailView.AddChild(hint);

        // 点任意处返回列表
        detailView.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                detailView.Visible = false;
        };
    }

    private void OpenDetail(NoteEntry note)
    {
        openNote = note;
        var state = StateOf(note);

        detailDate.Text = note.Date;
        detailTitle.Text = note.Title;

        foreach (var child in detailBody.GetChildren())
            child.QueueFree();
        foreach (var line in note.Body)
        {
            var l = new Label
            {
                Text = line,
                MouseFilter = MouseFilterEnum.Ignore,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            l.AddThemeFontSizeOverride("font_size", 24);
            l.AddThemeColorOverride("font_color", new Color(0.22f, 0.19f, 0.14f));
            detailBody.AddChild(l);
        }

        string footer = state?.Footer ?? "";
        if (!string.IsNullOrEmpty(state?.Extra))
            footer = string.IsNullOrEmpty(footer) ? state.Extra : footer + "\n" + state.Extra;
        detailFooter.Text = footer;
        // 暖色（已发出）用一点赭红，草稿用灰
        detailFooter.AddThemeColorOverride("font_color",
            state != null && state.Tint == "warm" ? new Color(0.56f, 0.33f, 0.22f) : InkGrey);

        detailView.Visible = true;
    }

    /// <summary>
    /// 这一条现在是什么态：按 ch16_final 的选项序号（0 发出 / 1 改成别的 / 2 删掉）。
    /// 没选过（还没到那一幕）→ 返回 null，按"刚写下的原样"显示。
    /// </summary>
    private static NoteState StateOf(NoteEntry note)
    {
        if (note.States == null || note.States.Count == 0)
            return null;
        int idx = GameManager.Instance?.GetChoice("ch16_final") ?? -1;
        string key = idx switch
        {
            0 => "sent",
            1 => "soft",
            2 => "deleted",
            _ => "",
        };
        if (string.IsNullOrEmpty(key))
            return null;
        return note.States.TryGetValue(key, out var st) ? st : null;
    }
}
