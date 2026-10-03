using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第一章：寄居蟹
///
/// 流程：白天实验室开场 → 淡入转场到川菜馆 → 探索两处（菜单/大家）
///      → 服务员来点菜 → 点菜选择（勇气值 ±）→ 各自后续 → 尾声（朋友圈）→ DEMO 结束页
/// </summary>
public partial class Ch01 : ChapterBase
{
    private const string ChapterId = "ch01"; // 对应 data/dialogues/ch01.json

    private readonly HashSet<string> foundItems = new();

    private Control hotspotsRoot;
    private Label hintLabel;

    protected override void OnChapterReady()
    {
        GD.Print("第一章：寄居蟹 —— 热闹是他们的，壳是自己的");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        GetNode<Button>("Hotspots/MenuBtn").Pressed += () => OnHotspotPressed("menu", "Hotspots/MenuBtn");
        GetNode<Button>("Hotspots/GroupBtn").Pressed += () => OnHotspotPressed("group", "Hotspots/GroupBtn");

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 章节标题缓缓淡出（不打断对白）
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    private void OnIntroFinished()
    {
        FadeToRestaurant();
    }

    /// <summary>
    /// 白天实验室 → 川菜馆的交叉淡入
    /// </summary>
    private void FadeToRestaurant()
    {
        var dayBg = GetNode<TextureRect>("DayLabBg");
        var restBg = GetNode<TextureRect>("RestaurantBg");

        restBg.Visible = true;
        restBg.Modulate = new Color(1, 1, 1, 0);

        var tween = CreateTween();
        tween.TweenProperty(restBg, "modulate:a", 1.0f, 1.2f);
        tween.TweenCallback(Callable.From(() =>
        {
            dayBg.Visible = false;
            DialogueManager.Instance.PlaySequence(ChapterId, "arrive", OnArriveFinished);
        }));
    }

    private void OnArriveFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        UpdateHint();
    }

    private void UpdateHint()
    {
        int n = foundItems.Count;
        hintLabel.Text = n >= 2
            ? "（场景里的东西都看过了）"
            : $"点击场景里的东西看看（{n}/2）";
    }

    private void OnHotspotPressed(string itemId, string buttonPath)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || foundItems.Contains(itemId))
            return;

        foundItems.Add(itemId);

        var btn = GetNode<Button>(buttonPath);
        btn.Text = "已看过";
        btn.Disabled = true;
        UpdateHint();

        dm.PlaySequence(ChapterId, $"explore_{itemId}", () =>
        {
            if (foundItems.Count >= 2)
            {
                PlayAskOrder();
            }
        });
    }

    private void PlayAskOrder()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "ask_order", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "order_food", OnOrderChosen);
        });
    }

    /// <summary>
    /// 点菜选择（index = 0/1/2）→ 后续 → 尾声 → DEMO 结束页
    /// </summary>
    private void OnOrderChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"order_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GameManager.Instance.SaveGame();
                GameManager.Instance.ChangeSceneWithTransition("res://scenes/ui/demo_end/demo_end.tscn");
            });
        });
    }
}
