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

    // 每个热点的柔光层（itemId → 柔光 Control，发呆提示用）
    private readonly Dictionary<string, Control> glows = new();

    // 发呆计时：12 秒没进展就给微光提示
    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第一章：寄居蟹 —— 热闹是他们的，壳是自己的");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        var menuBtn = GetNode<Button>("Hotspots/MenuBtn");
        var groupBtn = GetNode<Button>("Hotspots/GroupBtn");
        menuBtn.Pressed += () => OnHotspotPressed("menu", "Hotspots/MenuBtn");
        groupBtn.Pressed += () => OnHotspotPressed("group", "Hotspots/GroupBtn");

        // 隐形微光层（发呆提示用）
        glows["menu"] = HotspotGlow.Attach(menuBtn);
        glows["group"] = HotspotGlow.Attach(groupBtn);

        // "点万物有回应"：墙上的菜牌、门口的红灯笼、手边的茶碗（data/flavor/ch01.json）
        AddChild(FlavorSpots.Create(hotspotsRoot, "ch01"));

        // 全场景按钮音效
        UiSounds.WireAll(this);

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 章节标题缓缓淡出（不打断对白）
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>
    /// 每帧检查：玩家长时间没进展 & 没在看对话 → 让还没找到的热点轻轻呼吸一下
    /// </summary>
    public override void _Process(double delta)
    {
        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsBusy)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (!hotspotsRoot.Visible || foundItems.Count >= 2)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        PulseUnfoundHotspots();
    }

    private void PulseUnfoundHotspots()
    {
        float delay = 0f;
        foreach (var (itemId, glow) in glows)
        {
            if (foundItems.Contains(itemId))
                continue;
            HotspotGlow.Pulse(glow, delay);
            delay += 0.4f;
        }
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
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：锅还没开，妈先发来一句"钱够花吗"
        LiveEvents.Fire(this, "ch01", "ch01_ma_money");
    }

    private void UpdateHint()
    {
        int n = foundItems.Count;
        hintLabel.Text = n >= 2
            ? DataStore.Text("hint.ch01_done")
            : DataStore.Text("hint.ch01", n, 2);
    }

    private void OnHotspotPressed(string itemId, string buttonPath)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || foundItems.Contains(itemId))
            return;

        foundItems.Add(itemId);
        lastActivityMsec = Time.GetTicksMsec();

        // 点中的瞬间：指尖冒出两三颗小星光（"找到了！"的反馈，没有框）
        var btn = GetNode<Button>(buttonPath);
        HotspotGlow.Sparkle(btn);
        btn.Disabled = true; // 看过了：不再发光，也没有痕迹
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
    /// 点菜选择（index = 0/1/2）→ 后续 → 尾声 → 第二章《一千公里》
    /// </summary>
    private void OnOrderChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"order_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GoToNextChapter("res://scenes/chapters/ch02/ch02.tscn", 2);
            });
        });
    }
}
