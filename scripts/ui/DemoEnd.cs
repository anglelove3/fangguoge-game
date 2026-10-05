using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// DEMO 试玩结束页（第十一轮重做）
///
/// 【原来什么样 / 为什么改】
/// 旧版一进来就把「好感度 63 / 勇气值 41 / 当前结局倾向：寄居蟹」摊在屏幕上。
/// 试玩反馈 A4 说得很直接：数字一露出来，玩家立刻开始反推"哪道题该选哪个"，
/// 二次试玩就变成刷分，情绪全没了；而且提前把结局名字报出来，等于剧透。
///
/// 【现在什么样】
/// 只显示"这一路你是怎么选的"——每处选择配一句当时的提问，
/// 没玩到的部分写一句"（这一处，你还没玩到）"，全程不出现任何数字、不提结局。
/// 要回顾哪几处、每处怎么措辞，都在 data/text/demo_review.json 里改。
/// </summary>
public partial class DemoEnd : Control
{
    // 行样式（复用，不必每行都 new 一份字体）
    private static readonly Color ChapterColor = new(1f, 0.88f, 0.65f, 0.9f);
    private static readonly Color AnswerColor = new(1f, 0.97f, 0.92f);
    private static readonly Color MissingColor = new(0.6f, 0.62f, 0.7f, 0.75f);

    public override void _Ready()
    {
        var title = GetNode<Label>("Center/VBox/Title");
        var reviewTitle = GetNode<Label>("Center/VBox/ReviewTitle");
        var hint = GetNode<Label>("Center/VBox/HintLabel");
        title.Text = DataStore.Text("demo_end.title");
        reviewTitle.Text = DataStore.Text("demo_end.review_title");
        hint.Text = DataStore.Text("demo_end.hint");

        var box = GetNode<VBoxContainer>("Center/VBox/ReviewScroll/ReviewBox");
        BuildReview(box);

        GetNode<Button>("Center/VBox/BackButton").Pressed += OnBackPressed;
        GetNode<Button>("Center/VBox/BackButton").Text = DataStore.Text("demo_end.back");

        // 按钮音效
        UiSounds.WireAll(this);

        // 结束页淡入（原来一睁眼就是黑底白字，有点吓人）
        Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1f, 0.6);

        GD.Print("[DEMO结束] 选择回顾页已展示");
    }

    /// <summary>
    /// 按 data/text/demo_review.json 的清单，把玩家做过的选择一条一条列出来
    /// </summary>
    private void BuildReview(VBoxContainer box)
    {
        var history = GameManager.Instance.ChoiceHistory;
        var dm = DialogueManager.Instance;

        foreach (var item in DataStore.ReviewItems)
        {
            int index = history.TryGetValue(item.Choice, out var saved) ? saved : -1;
            string answer = index >= 0 && dm != null
                ? dm.GetChoiceOptionText(item.Chapter, item.GroupId, index)
                : "";

            box.AddChild(MakeRow(item.Where, item.Label, answer));
        }
    }

    /// <summary>一行回顾：左边"第几章"，右边"那处选择你答的是……"</summary>
    private Control MakeRow(string where, string question, string answer)
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(1080, 0),
        };
        row.AddThemeConstantOverride("separation", 26);

        var chapter = new Label
        {
            Text = where,
            CustomMinimumSize = new Vector2(240, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chapter.AddThemeFontSizeOverride("font_size", 22);
        chapter.AddThemeColorOverride("font_color", ChapterColor);
        row.AddChild(chapter);

        // 竖线，把"章节"和"内容"隔开一点
        var bar = new ColorRect
        {
            Color = new Color(1, 0.88f, 0.65f, 0.18f),
            CustomMinimumSize = new Vector2(2, 44),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(bar);

        bool got = !string.IsNullOrEmpty(answer);
        var body = new Label
        {
            Text = got
                ? $"{question}\n「{answer}」"
                : $"{question}\n{DataStore.Text("demo_end.review_missing")}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeFontSizeOverride("font_size", 22);
        body.AddThemeColorOverride("font_color", got ? AnswerColor : MissingColor);
        row.AddChild(body);

        // 一条条浮上来（和选项面板同一股"发牌"味道）
        row.Modulate = new Color(1, 1, 1, 0);
        var tween = row.CreateTween();
        tween.TweenProperty(row, "modulate:a", 1f, 0.35);

        return row;
    }

    private void OnBackPressed()
    {
        GameManager.Instance.ReturnToMainMenu();
    }
}
