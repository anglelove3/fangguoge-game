using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 序章：那首歌
/// 继承 ChapterBase，自动拥有标题显示、返回菜单、自动存档等功能
///
/// 流程：标题卡 → 开场对白 → 探索三件物品（电脑/耳机/窗外）
///      → 齐了之后室友给外号起选项 → 选完的后续对白 → 进入第一章
///
/// 【知识点 - 回调链】
/// PlaySequence / ShowChoice 的最后一个参数是"结束后要做什么"，
/// 一层套一层形成流程链，就像多米诺骨牌一样一张推一张。
/// </summary>
public partial class Prologue : ChapterBase
{
    private const string ChapterId = "prologue"; // 对应 data/dialogues/prologue.json

    // 已经看过的物品（用 Set 防止重复触发）
    private readonly HashSet<string> foundItems = new();

    private Control hotspotsRoot;
    private Label hintLabel;
    private Control titleCard;

    public override void _Ready()
    {
        base._Ready(); // 基类负责找节点、绑定返回按钮、存档
    }

    /// <summary>
    /// 重写基类的虚方法，写序章特有的逻辑
    /// </summary>
    protected override void OnChapterReady()
    {
        GD.Print("序章：那首歌 —— 深夜实验室，循环播放《开始懂了》");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        titleCard = GetNode<Control>("TitleCard");

        // 绑定三个物品热点按钮
        GetNode<Button>("Hotspots/ComputerBtn").Pressed += () => OnHotspotPressed("computer", "Hotspots/ComputerBtn");
        GetNode<Button>("Hotspots/EarphonesBtn").Pressed += () => OnHotspotPressed("earphones", "Hotspots/EarphonesBtn");
        GetNode<Button>("Hotspots/WindowBtn").Pressed += () => OnHotspotPressed("window", "Hotspots/WindowBtn");

        // 标题卡：点任意位置开始
        var startOverlay = GetNode<Button>("TitleCard/StartOverlay");
        startOverlay.Pressed += BeginIntro;

        // 开场先藏起热点提示
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
    }

    /// <summary>
    /// 标题卡淡出 → 播放开场对白
    /// </summary>
    private void BeginIntro()
    {
        var startOverlay = GetNode<Button>("TitleCard/StartOverlay");
        startOverlay.Disabled = true; // 防止连点触发两次

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleCard, "modulate:a", 0.0f, 0.7f);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 0.7f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 0.7f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            titleCard.Visible = false;
            titleLabel.Visible = false;
            subtitleLabel.Visible = false;

            DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
        }));
    }

    /// <summary>
    /// 开场对白播完 → 显示热点，进入探索阶段
    /// </summary>
    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        UpdateHint();
    }

    private void UpdateHint()
    {
        int n = foundItems.Count;
        hintLabel.Text = n >= 3
            ? "（场景里的东西都看过了）"
            : $"点击场景中的物品看看（{n}/3）";
    }

    /// <summary>
    /// 点击物品热点：播放对应的探索对白，看完后按钮变灰
    /// </summary>
    private void OnHotspotPressed(string itemId, string buttonPath)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || foundItems.Contains(itemId))
            return; // 对话进行中或已经看过 → 忽略

        foundItems.Add(itemId);

        var btn = GetNode<Button>(buttonPath);
        btn.Text = "已看过";
        btn.Disabled = true;
        UpdateHint();

        dm.PlaySequence(ChapterId, $"explore_{itemId}", () =>
        {
            // 三样都看完 → 室友回来给外号选项
            if (foundItems.Count >= 3)
            {
                PlayAllFound();
            }
        });
    }

    private void PlayAllFound()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "all_found", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "nickname", OnNicknameChosen);
        });
    }

    /// <summary>
    /// 外号选完（index = 0/1/2）→ 各自的后续对白 → 尾声 → 第一章
    /// </summary>
    private void OnNicknameChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"nickname_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GoToNextChapter("res://scenes/chapters/ch01/ch01.tscn", 1);
            });
        });
    }
}
