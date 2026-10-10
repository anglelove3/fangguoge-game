using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// "手机活起来"（第十二轮）
///
/// 【这是什么】
/// 玩剧情的时候，手机会自己"震"一下——有人在微信里找你。
/// 右上角飘过一条很轻的提示，主角的手机热点上亮起小红点；
/// 等玩家真的打开手机，那条消息就躺在会话里，像它一直在那儿等你。
///
/// 会在外面震你的不只恋人：妈会问你钱够不够花，发小会喊你回家喝酒，
/// 凌晨的 li-lab 群里三金师兄还在报仪器空档……
/// 这条消息和主线无关，但世界因此显得"你没看手机的时候，它也在动"。
///
/// 【数据在哪】
/// data/chat/live_events.json：每条事件 = id + 章节 + 联系人 + 正文 + 送达延迟。
///
/// 【怎么不重复】
///   1) 触发时事件 id 记进存档（GameManager.TriggeredLiveEvents），重玩同一章不会再来一遍；
///   2) 消息本体带 LiveEventId，往会话里补的时候天然去重（退出重进也不重复）；
///   3) 手机第一次打开时会"补投递"——存档里记过、没送到你眼前的，一并补上（按时间顺序）。
/// </summary>
public static class LiveEvents
{
    private class EventDef
    {
        public string Id { get; set; } = "";
        public string Chapter { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Sender { get; set; } = "";   // 群消息的发言人（留空 = 私聊）
        public string Text { get; set; } = "";
        public float Delay { get; set; } = 5f;     // 触发后几秒送达
        public string Toast { get; set; } = "";    // 右上角提示（留空 = 默认文案）
        public string Item { get; set; } = "";     // 送达时顺带进背包的物品 id（留空 = 不给东西）
    }

    private class LiveEventsFile
    {
        public List<EventDef> Events { get; set; } = new();
    }

    private static LiveEventsFile data;

    /// <summary>未读计数：联系人 id → 条数（手机里的红点用）</summary>
    private static readonly Dictionary<string, int> unread = new();

    private static LiveEventsFile GetData()
    {
        data ??= DataStore.LoadJson<LiveEventsFile>("res://data/chat/live_events.json") ?? new LiveEventsFile();
        return data;
    }

    // ==================== 对外 API（章节脚本调用） ====================

    /// <summary>
    /// 在章节里触发一条动态事件：几秒后手机"震一下"，消息落进对应会话。
    /// 已经触发过的（存档记着）会被安静地跳过。
    /// </summary>
    public static void Fire(Node host, string chapterId, string eventId)
    {
        if (host == null || !GodotObject.IsInstanceValid(host))
            return;
        if (GameManager.Instance?.HasLiveEvent(eventId) == true)
            return;

        var def = GetData().Events.Find(e => e.Id == eventId);
        if (def == null)
        {
            GD.PrintErr($"[动态事件] 找不到事件：{eventId}");
            return;
        }

        float delay = Mathf.Max(0.5f, def.Delay);
        GD.Print($"[动态事件] {chapterId} 排期 {def.Id}（{delay:0.#} 秒后送达）");

        host.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(host))
            {
                // 章节已经翻篇（节点没了）：飘字/震感没地方放，但消息本体不能丢——
                // 落库 + 记存档；下次打开手机，红点和消息都在（补投递天然去重）。
                GameManager.Instance?.RecordLiveEvent(def.Id);
                PushMessage(def);
                GrantItemIfAny(def);
                return;
            }
            Deliver(host, def);
        };
    }

    /// <summary>手机打开时的"补投递"：存档里触发过、但还没送到会话里的，一并补上</summary>
    public static void EnsureDelivered()
    {
        var triggered = GameManager.Instance?.TriggeredLiveEvents;
        if (triggered == null)
            return;
        foreach (var id in triggered)
        {
            var def = GetData().Events.Find(e => e.Id == id);
            if (def != null)
            {
                PushMessage(def);
                GrantItemIfAny(def); // 老存档：消息补投递时，挂着的物品（请帖）也一并补进背包
            }
        }
    }

    /// <summary>本联系人未读几条（会话列表红点用）</summary>
    public static int UnreadFor(string contactId) =>
        !string.IsNullOrEmpty(contactId) && unread.TryGetValue(contactId, out var n) ? n : 0;

    /// <summary>有没有任何未读（手机热点小红点用）</summary>
    public static bool HasAnyUnread => unread.Count > 0;

    /// <summary>全部未读条数合计（锁屏通知卡 / 桌面微信角标用）</summary>
    public static int TotalUnread
    {
        get
        {
            int total = 0;
            foreach (var kv in unread)
                total += kv.Value;
            return total;
        }
    }

    /// <summary>玩家点开了某位联系人的聊天 → 未读清零</summary>
    public static void ClearUnread(string contactId)
    {
        if (!string.IsNullOrEmpty(contactId))
            unread.Remove(contactId);
    }

    /// <summary>
    /// 给手机热点挂一个"未读小红点"：它每 0.5 秒自己看一眼有没有未读。
    /// （挂在按钮右上角，手机上没消息时完全不可见。）
    /// </summary>
    public static void AttachPhoneDot(Button phoneBtn)
    {
        if (phoneBtn == null || !GodotObject.IsInstanceValid(phoneBtn))
            return;
        var dot = new PhoneUnreadDot();
        dot.Name = "PhoneUnreadDot"; // 纯代码 new 默认叫 "@Control@N"，显式命名方便查找
        phoneBtn.AddChild(dot);
    }

    // ==================== 内部 ====================

    private static void Deliver(Node host, EventDef def)
    {
        GameManager.Instance?.RecordLiveEvent(def.Id);
        PushMessage(def);
        GrantItemIfAny(def);
        PlayPhoneBuzz(host);
        ShowToast(host, def);
    }

    /// <summary>这条动态事件挂着物品（比如请帖）就放进背包；没送到的（补投递路径）也在这里补上</summary>
    private static void GrantItemIfAny(EventDef def)
    {
        if (!string.IsNullOrEmpty(def.Item))
            GameManager.Instance?.AddItem(def.Item);
    }

    /// <summary>把消息落进会话（幂等：同一条 LiveEventId 只落一次）+ 记未读</summary>
    private static void PushMessage(EventDef def)
    {
        var msg = new ChatMessageData
        {
            Sender = "other",
            Type = "text",
            Text = def.Text,
            SenderName = def.Sender,
            LiveEventId = def.Id,
        };
        // 只有"真的落进会话"才算未读——补投递时同一条会去重返回 false，
        // 不拦一下的话每开一次手机红点就 +1，读完也消不掉。
        if (!ChatOverlay.StoreIncoming(def.Contact, msg))
            return;
        unread.TryGetValue(def.Contact, out int n);
        unread[def.Contact] = n + 1;
    }

    /// <summary>手机震感：两下很轻的"嗡"（第二下稍晚、更轻）</summary>
    private static void PlayPhoneBuzz(Node host)
    {
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -13f);
        if (GodotObject.IsInstanceValid(host))
        {
            host.GetTree().CreateTimer(0.22).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(host))
                    AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -19f, 0.06f);
            };
        }
    }

    /// <summary>右上角轻轻飘过一条提示（2.6 秒后自己走）</summary>
    private static void ShowToast(Node host, EventDef def)
    {
        string text = string.IsNullOrEmpty(def.Toast) ? DataStore.Text("live.toast_default") : def.Toast;

        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 90 };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.12f, 0.82f),
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            ShadowColor = new Color(0, 0, 0, 0.25f),
            ShadowSize = 8,
            ShadowOffset = new Vector2(0, 3),
        };
        chip.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(0.98f, 0.97f, 0.93f));
        chip.AddChild(label);

        // 右上角锚定（HintLabel 在顶部中间、通话提示在中右上，这条消息贴最上面）
        chip.AnchorLeft = 1f;
        chip.AnchorRight = 1f;
        chip.AnchorTop = 0f;
        chip.AnchorBottom = 0f;
        chip.GrowHorizontal = Control.GrowDirection.Begin;
        chip.OffsetTop = 22f;
        chip.OffsetRight = -28f;
        chip.Modulate = new Color(1, 1, 1, 0);

        host.AddChild(chip);

        // 等一帧拿到实际宽度，再摆到右边缘（GrowBegin 也会兜住，这里不依赖它）
        var tween = chip.CreateTween();
        tween.TweenProperty(chip, "modulate:a", 1.0f, 0.28f).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(chip, "position:y", (float)chip.Position.Y + 10f, 0.28f)
            .From((float)chip.Position.Y)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenInterval(2.6f);
        tween.TweenProperty(chip, "modulate:a", 0f, 0.45f).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(chip.QueueFree));
    }
}

/// <summary>手机热点右上角的未读小红点（自己盯着 LiveEvents.HasAnyUnread）</summary>
public partial class PhoneUnreadDot : Control
{
    private ulong lastPoll;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -18;
        OffsetTop = -6;
        OffsetRight = 16;
        OffsetBottom = 28;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        // 每半秒看一眼（不用每帧惊动静态表）
        if (Time.GetTicksMsec() - lastPoll < 500)
            return;
        lastPoll = Time.GetTicksMsec();
        bool show = LiveEvents.HasAnyUnread;
        if (show != Visible)
        {
            Visible = show;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var c = Size / 2f;
        DrawCircle(c, Mathf.Min(Size.X, Size.Y) / 2f, new Color(0.92f, 0.25f, 0.20f));
        DrawCircle(c, Mathf.Min(Size.X, Size.Y) / 2f - 2.5f, new Color(1f, 0.45f, 0.4f, 0.55f));
    }
}
