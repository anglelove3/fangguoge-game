using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 第十六章的蒙太奇演出：把 3~7 月六个日子快闪一遍。
///
/// 【为什么要这一段】
/// 第十五章结束在 3 月 8 日日出前，第十六章主戏在 7 月台风天。中间四个月不能空着，
/// 否则"她毕业回家了"这件事没有重量。用六张卡：日期 + 场景 + 聊天记录 + 一句旁白，
/// 三分钟讲完四个月。
///
/// 【节奏】
/// 每张卡：淡入 0.5s → 聊天气泡逐条 0.85s 一条 → 停 1.1s → 淡出 0.45s。
/// 点屏幕 = 加速（当前卡立即全部显示，再点进下一张）。全片约 2.5~3 分钟，点着看约 1 分钟。
///
/// 【数据】data/dialogues/ch16.json 的 montage.cards（DataStore 读，改文案不碰代码）
/// </summary>
public partial class MontageReel : Control
{
    // ==================== 数据 ====================

    public class MontageChat
    {
        public string Sender { get; set; } = "";    // "me" / "other"
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class MontageCard
    {
        public string Date { get; set; } = "";
        public string Scene { get; set; } = "";
        public List<MontageChat> Chat { get; set; } = new();
        public string Note { get; set; } = "";
    }

    public class MontageSection
    {
        public List<MontageCard> Cards { get; set; } = new();
    }

    public class MontageFile
    {
        // ch16.json 的顶层结构：{ "_comment":..., "montage": { "cards": [...] }, ... }
        public MontageSection Montage { get; set; } = new();
    }

    // ==================== 事件 ====================

    /// <summary>整段播完（由 Ch16 接下去进实验室）</summary>
    public event Action Finished;

    // ==================== 状态 ====================

    private class Section
    {
        public Control Root;
        public VBoxContainer ChatCol;
        public Label Note;
    }

    private Control stage;          // 每张卡重建的内容层
    private Section current;
    private bool skipRequested;
    private ulong lastSkipMsec;

    private static readonly Color BgColor = new(0.043f, 0.051f, 0.071f);
    private static readonly Color DateColor = new(0.788f, 0.698f, 0.541f);
    private static readonly Color SceneColor = new(0.62f, 0.65f, 0.70f);
    private static readonly Color NoteColor = new(0.55f, 0.58f, 0.63f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = BgColor, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        stage = new Control { MouseFilter = MouseFilterEnum.Ignore };
        stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(stage);

        // 点屏幕加速
        GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                // 0.35 秒冷却，避免连点一路跳到底
                if (Time.GetTicksMsec() - lastSkipMsec < 350)
                    return;
                lastSkipMsec = Time.GetTicksMsec();
                skipRequested = true;
            }
        };
    }

    /// <summary>开播（读数据 → 逐张放）</summary>
    public void Begin()
    {
        var file = DataStore.LoadJson<MontageFile>("res://data/dialogues/ch16.json");
        List<MontageCard> cards = file?.Montage?.Cards ?? new List<MontageCard>();
        if (cards.Count == 0)
        {
            Finished?.Invoke();
            return;
        }
        GD.Print($"[蒙太奇] 共 {cards.Count} 张卡；第一张 chat={(cards.Count > 0 ? (cards[0].Chat?.Count ?? -1) : -1)}");
        _ = PlayAll(cards);
    }

    private async Task PlayAll(List<MontageCard> cards)
    {
        try
        {
            foreach (var card in cards)
            {
                if (!IsInsideTree())
                    return;
                skipRequested = false;
                BuildCard(card);
                await FadeStage(1f, 0.5f);

                foreach (var msg in card.Chat)
                {
                    if (!IsInsideTree())
                        return;
                    if (!skipRequested)
                        await Delay(0.85f);
                    if (!IsInstanceValid(current?.ChatCol))
                        return;
                    AddBubble(msg);
                }

                if (!skipRequested)
                    await Delay(1.1f);
                await FadeStage(0f, 0.45f);
            }

            if (!IsInsideTree())
                return;
            Finished?.Invoke();
        }
        catch (Exception)
        {
            // 章节被切走（节点释放）→ 什么都不做
        }
    }

    // ==================== 卡片 ====================

    private void BuildCard(MontageCard card)
    {
        foreach (var child in stage.GetChildren())
            child.QueueFree();

        var root = new Control { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stage.AddChild(root);

        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.OffsetLeft = 0;
        pad.OffsetRight = 0;
        pad.AddThemeConstantOverride("margin_left", 180);
        pad.AddThemeConstantOverride("margin_right", 180);
        pad.AddThemeConstantOverride("margin_top", 96);
        pad.AddThemeConstantOverride("margin_bottom", 72);
        root.AddChild(pad);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        pad.AddChild(col);

        var date = new Label
        {
            Text = card.Date,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        date.AddThemeFontSizeOverride("font_size", 62);
        date.AddThemeColorOverride("font_color", DateColor);
        col.AddChild(date);

        var scene = new Label
        {
            Text = card.Scene,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        scene.AddThemeFontSizeOverride("font_size", 26);
        scene.AddThemeColorOverride("font_color", SceneColor);
        col.AddChild(scene);

        var rule = new ColorRect
        {
            Color = new Color(0.30f, 0.32f, 0.36f, 0.7f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var ruleWrap = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        ruleWrap.AddThemeConstantOverride("margin_top", 14);
        ruleWrap.AddThemeConstantOverride("margin_bottom", 14);
        ruleWrap.AddChild(rule);
        col.AddChild(ruleWrap);

        var chatCol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chatCol.AddThemeConstantOverride("separation", 10);
        chatCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(chatCol);

        var note = new Label
        {
            Text = card.Note,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeFontSizeOverride("font_size", 22);
        note.AddThemeColorOverride("font_color", NoteColor);
        note.Modulate = new Color(1, 1, 1, 0);
        var noteWrap = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        noteWrap.AddThemeConstantOverride("margin_top", 26);
        noteWrap.AddChild(note);
        col.AddChild(noteWrap);

        current = new Section { Root = root, ChatCol = chatCol, Note = note };
        note.Modulate = new Color(1, 1, 1, 1);
    }

    private void AddBubble(MontageChat msg)
    {
        bool mine = msg.Sender == "me";

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.CustomMinimumSize = new Vector2(0, 58);

        // 固定最小宽度：Label 在无宽度约束的容器里会把中文挤成竖排（2026-10-21 实测）
        var bubble = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(320, 52),
        };
        var style = new StyleBoxFlat
        {
            BgColor = mine ? new Color(0.32f, 0.27f, 0.20f) : new Color(0.16f, 0.18f, 0.22f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        bubble.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = msg.Text,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 23);
        label.AddThemeColorOverride("font_color", mine ? new Color(0.95f, 0.92f, 0.85f) : new Color(0.90f, 0.92f, 0.95f));
        bubble.AddChild(label);

        if (mine)
        {
            row.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            row.AddChild(bubble);
        }
        else
        {
            row.AddChild(bubble);
            row.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        }

        row.Modulate = new Color(1, 1, 1, 1);
        current.ChatCol.AddChild(row);
    }

    // ==================== 工具 ====================

    private async Task FadeStage(float to, float dur)
    {
        if (!IsInstanceValid(current?.Root))
            return;
        var tw = CreateTween();
        tw.TweenProperty(current.Root, "modulate:a", to, dur);
        await ToSignal(tw, Tween.SignalName.Finished);
    }

    private async Task Delay(float sec)
    {
        await ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);
    }
}
