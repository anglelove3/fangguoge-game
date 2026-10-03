using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第二章：一千公里
///
/// 流程：深夜宿舍开场 → 探索两处（手机聊天记录 / 窗外）
///      → 室友去睡 → 回复外地的她（选择）→ 视频通话（来电演出）
///      → 通话中的选择 → 尾声 → DEMO 结束页
///
/// 【本章新知识点 - 用 Tween 做循环动画】
/// "视频通话中……"的提示会呼吸闪烁，用 SetLoops() 让一段淡入淡出无限循环。
/// </summary>
public partial class Ch02 : ChapterBase
{
    private const string ChapterId = "ch02"; // 对应 data/dialogues/ch02.json

    private readonly HashSet<string> foundItems = new();

    private Control hotspotsRoot;
    private Label hintLabel;
    private Label callLabel;
    private Tween callLabelTween;

    private readonly Dictionary<string, Panel> glows = new();

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第二章：一千公里 —— 隔着一千公里，和一句没说完的晚安");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        callLabel = GetNode<Label>("CallLabel");

        var phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        var windowBtn = GetNode<Button>("Hotspots/WindowBtn");
        phoneBtn.Pressed += () => OnHotspotPressed("phone", "Hotspots/PhoneBtn");
        windowBtn.Pressed += () => OnHotspotPressed("window", "Hotspots/WindowBtn");

        glows["phone"] = HotspotGlow.Attach(phoneBtn);
        glows["window"] = HotspotGlow.Attach(windowBtn);

        UiSounds.WireAll(this);

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 章节标题缓缓淡出
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 未找到的热点轻轻呼吸</summary>
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
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();
    }

    private void UpdateHint()
    {
        int n = foundItems.Count;
        hintLabel.Text = n >= 2
            ? "（宿舍里该看的都看过了）"
            : $"晚上十点的宿舍……好像有什么在等着你（{n}/2）";
    }

    private void OnHotspotPressed(string itemId, string buttonPath)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || foundItems.Contains(itemId))
            return;

        foundItems.Add(itemId);
        lastActivityMsec = Time.GetTicksMsec();

        if (glows.TryGetValue(itemId, out var glow))
            HotspotGlow.Pulse(glow);

        var btn = GetNode<Button>(buttonPath);
        btn.Disabled = true;
        UpdateHint();

        dm.PlaySequence(ChapterId, $"explore_{itemId}", () =>
        {
            if (foundItems.Count >= 2)
            {
                PlayAllFound();
            }
        });
    }

    private void PlayAllFound()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "all_found", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "reply", OnReplyChosen);
        });
    }

    /// <summary>回复方式选完 → 各自的后续 → 视频通话来啦</summary>
    private void OnReplyChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"reply_{index}", StartPhoneCall);
    }

    /// <summary>
    /// 视频通话小演出：来电对白 → 接通后右上角亮起"视频通话中……"（呼吸闪烁）
    /// </summary>
    private void StartPhoneCall()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "call_arrive", () =>
        {
            // 接通了：右上角亮起通话提示，呼吸闪烁
            callLabel.Visible = true;
            callLabel.Modulate = new Color(1, 1, 1, 0);
            callLabelTween?.Kill();
            callLabelTween = CreateTween();
            callLabelTween.SetLoops();
            callLabelTween.TweenProperty(callLabel, "modulate:a", 0.45f, 0.9f)
                .SetTrans(Tween.TransitionType.Sine);
            callLabelTween.TweenProperty(callLabel, "modulate:a", 1.0f, 0.9f)
                .SetTrans(Tween.TransitionType.Sine);

            DialogueManager.Instance.PlaySequence(ChapterId, "call_talk", () =>
            {
                DialogueManager.Instance.ShowChoice(ChapterId, "say_more", OnSayChosen);
            });
        });
    }

    /// <summary>通话中的选择 → 各自的后续 → 挂断（收起通话提示）→ 尾声 → 结束页</summary>
    private void OnSayChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"say_{index}", () =>
        {
            // 挂断：通话提示收起来
            callLabelTween?.Kill();
            callLabel.Visible = false;

            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GameManager.Instance.SaveGame();
                GameManager.Instance.ChangeSceneWithTransition("res://scenes/ui/demo_end/demo_end.tscn");
            });
        });
    }
}
