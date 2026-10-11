using Godot;
using System.Collections.Generic;

/// <summary>
/// 第九章：置顶
///
/// 流程（五幕，跨一周）：
///   第 1 幕 周一夜·宿舍 —— 群里催进展 → 长按她的会话【置顶】（玩家自己在微信里按）
///                          → 心声：怎么跟自己解释 → 睡过这一晚
///   第 2 幕 周三·实验室 —— 手机扣过去三次 → 三条消息回哪句（busy）
///   第 3 幕 周四深夜      —— 她说「没事」→ 内心解读：这三个字怎么读（read，ReadCard 新机制）
///                          → 论文被否第三版 / 想见 → 心声 → 睡
///   第 4 幕 周六·河堤     —— 这一次他说了算（plan）→ 桥洞风铃 → 礼花筒终于拉响
///                          （第八章塞过来的那枚，MarkItemUsed 落档）
///   第 5 幕 周六夜·宿舍   —— 「今天很好。」→ 滑到底又滑回来，置顶的她还在最上面 → 尾声
///
/// 【这一章的两个"第一次"】
///   1. 第一次内心解读（ReadCard）：选项不是三句话，而是三种"读法"——
///      引用她的原话摆在上面，三种理解摆在下面，选完才说出对应的那句回复。
///   2. 聊天记录第一次跨五天累积：本章的聊天消息全部走 gateChoice 幕门控
///      （幕开始时 RecordGate 解锁），不走 LiveEvents——补投递只会追加在列表末尾，
///      跟门控消息混排会在重载后乱序。"手机活了"用 GiveUnread 的小红点来演。
///
/// 【人物不写死在代码里】
/// 置顶谁、开哪份聊天剧本、礼花筒是哪件物品、每一幕解锁哪把幕门——全部在 data/chapters/ch09.json。
/// </summary>
public partial class Ch09 : ChapterBase
{
    private const string ChapterId = "ch09";

    // ==================== 章节参数（data/chapters/ch09.json） ====================

    private class ChapterConfig
    {
        public string JieContact { get; set; } = "";
        public string JieChat { get; set; } = "";
        public string GroupContact { get; set; } = "";
        public string GroupChat { get; set; } = "";
        public string PopperItem { get; set; } = "";
        public Dictionary<string, string> Gates { get; set; } = new();
    }

    private ChapterConfig cfg = new();

    /// <summary>
    /// 解锁一幕的聊天幕门：这幕开始时记一次选择，聊天里带这把钥匙的消息从此可见。
    /// （不真记进"选择回顾"——选项回顾只收 ShowChoice / 心声的正式选择。）
    /// </summary>
    private void RecordGate(string slot)
    {
        if (cfg.Gates != null && cfg.Gates.TryGetValue(slot, out var key) && !string.IsNullOrEmpty(key))
            RecordChoice(key, 0);
    }

    // ==================== 节点 ====================

    private TextureRect bgDorm;
    private TextureRect bgLab;
    private TextureRect bgRiver;

    private Control hotspotsRoot;
    private Control flavorDorm;
    private Control flavorLab;
    private Control flavorRiver;
    private Label hintLabel;

    // 宿舍（第 1 / 3 / 5 幕共用一间）
    private Button phoneBtn;
    private Button bedBtn;
    // 实验室（第 2 幕）
    private Button labPhoneBtn;
    private Button labExitBtn;
    // 河堤（第 4 幕）
    private Button bridgeBtn;

    private Control phoneGlow;
    private Control bedGlow;
    private Control labPhoneGlow;
    private Control labExitGlow;
    private Control bridgeGlow;

    private AudioStreamPlayer windPlayer; // 河堤的风：进第 4 幕淡入，回宿舍淡出

    // ==================== 进度 ====================

    private enum Phase { Dorm1, Lab, Night2, River, Night3 }
    private Phase phase = Phase.Dorm1;

    private bool phoneArmed;     // 第 1 幕：那段"点开手机"的引导只演一次
    private bool pinDone;        // 第 1 幕：玩家真的把她置顶了（pin 演完）
    private bool labOpenPlayed;  // 第 2 幕：翻手机前的引导只演一次
    private bool busyChosen;     // 第 2 幕：三条消息已经回了
    private bool labDone;        // 第 2 幕：收尾演完，门开了
    private bool night2OpenPlayed; // 第 3 幕：翻手机前的引导只演一次
    private bool readChosen;     // 第 3 幕：「没事」已经读过了
    private int readIndex;       // 第 3 幕：读成了哪一种（0/1/2，河堤开场跟着变）
    private bool planChosen;     // 第 4 幕：去哪儿已经定了
    private bool popperDone;     // 第 4 幕：礼花筒拉过了
    private bool finished;       // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第九章：置顶 —— 一抬头就能看见");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch09.json") ?? new ChapterConfig();

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -16f, 1.5f);

        bgDorm = GetNode<TextureRect>("Background");
        bgLab = GetNode<TextureRect>("Lab");
        bgRiver = GetNode<TextureRect>("River");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        bedBtn = GetNode<Button>("Hotspots/BedBtn");
        labPhoneBtn = GetNode<Button>("Hotspots/LabPhoneBtn");
        labExitBtn = GetNode<Button>("Hotspots/LabExitBtn");
        bridgeBtn = GetNode<Button>("Hotspots/BridgeBtn");

        phoneBtn.Pressed += OnDormPhonePressed;
        bedBtn.Pressed += OnBedPressed;
        labPhoneBtn.Pressed += OnLabPhonePressed;
        labExitBtn.Pressed += OnLabExitPressed;
        bridgeBtn.Pressed += OnBridgePressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        bedGlow = HotspotGlow.Attach(bedBtn);
        labPhoneGlow = HotspotGlow.Attach(labPhoneBtn);
        labExitGlow = HotspotGlow.Attach(labExitBtn);
        bridgeGlow = HotspotGlow.Attach(bridgeBtn);

        // 热点位置按"美术图比例"登记（背景 cover 缩放时也跟着贴在原处）
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f)); // 桌上那台手机
        ArtAnchor.TrackFraction(bedBtn, new Rect2(0.44f, 0.10f, 0.26f, 0.10f));       // 上铺
        ArtAnchor.TrackFraction(labPhoneBtn, new Rect2(0.544f, 0.771f, 0.095f, 0.098f));
        ArtAnchor.TrackFraction(labExitBtn, new Rect2(0.708f, 0.40f, 0.06f, 0.19f));   // 实验室的门
        ArtAnchor.TrackFraction(bridgeBtn, new Rect2(0.70f, 0.28f, 0.18f, 0.30f));     // 桥洞那边

        // "点万物有回应"：三个场景各一套，跟着幕开关
        flavorDorm = MakeFlavorLayer("ch09_dorm");
        flavorLab = MakeFlavorLayer("ch09_lab");
        flavorRiver = MakeFlavorLayer("ch09_river");

        // "手机活起来"：两台手机都挂小红点（本章没有动态事件，只有未读数）
        LiveEvents.AttachPhoneDot(phoneBtn);
        LiveEvents.AttachPhoneDot(labPhoneBtn);

        // 河堤的风：程序生成的无缝循环（沿用婚宴人声那套写法）
        windPlayer = new AudioStreamPlayer { VolumeDb = -60f };
        var windStream = GD.Load<AudioStream>(AudioManager.SfxRiverWindLoop);
        if (windStream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        windPlayer.Stream = windStream;
        AddChild(windPlayer);

        UiSounds.WireAll(this);

        // 第 1 幕用章节主时刻（11 月 3 日 周一 22:15），时段先清干净
        StoryClock.ClearSegment();

        // 开场只留宿舍这一层：手机先亮着，置顶完了床才出现
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        phoneBtn.Visible = true;
        flavorLab.Visible = false;
        flavorRiver.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>建一层风味热点（"点万物有回应"），挂到 Hotspots 下面</summary>
    private Control MakeFlavorLayer(string flavorId)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hotspotsRoot.AddChild(layer);
        // 风味层垫到最底下：剧情主热区永远优先接住点击（和第七 / 八章同一条规矩）
        hotspotsRoot.MoveChild(layer, 0);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    /// <summary>换幕之前先把上一幕的热点全收掉</summary>
    private void HideAllHotspots()
    {
        foreach (var btn in new[] { phoneBtn, bedBtn, labPhoneBtn, labExitBtn, bridgeBtn })
        {
            if (btn != null)
                btn.Visible = false;
        }
    }

    /// <summary>出了这一章，章内时段作废</summary>
    public override void _ExitTree()
    {
        StoryClock.ClearSegment();
    }

    // ==================== 发呆提示 ====================

    /// <summary>发呆 12 秒 → 当前这一幕"还没点的地方"轻轻呼吸</summary>
    public override void _Process(double delta)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (!hotspotsRoot.Visible || finished)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();

        float delay = 0f;
        foreach (var spot in PendingSpots())
        {
            HotspotGlow.Pulse(spot, delay);
            delay += 0.4f;
        }
    }

    /// <summary>这一幕里"还该点、还没点完"的地方（发呆时挨个眨一下光）</summary>
    private List<Control> PendingSpots()
    {
        var list = new List<Control>();
        switch (phase)
        {
            case Phase.Dorm1:
                if (!pinDone) Add(list, phoneGlow);
                else Add(list, bedGlow);
                break;

            case Phase.Lab:
                if (!labDone) Add(list, labPhoneGlow);
                else Add(list, labExitGlow);
                break;

            case Phase.Night2:
                if (!readChosen) Add(list, phoneGlow);
                else Add(list, bedGlow);
                break;

            case Phase.River:
                if (planChosen && !popperDone) Add(list, bridgeGlow);
                break;

            case Phase.Night3:
                Add(list, phoneGlow);
                Add(list, bedGlow);
                break;
        }
        return list;
    }

    private static void Add(List<Control> list, Control glow)
    {
        if (glow != null && GodotObject.IsInstanceValid(glow) && glow.IsVisibleInTree())
            list.Add(glow);
    }

    // ==================== 提示语 ====================

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        flavorDorm.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁周一晚的聊天（她的围巾消息 + 群里的起哄），小红点跟着亮
        RecordGate("dorm1");
        LiveEvents.GiveUnread(cfg.JieContact, 1);
        LiveEvents.GiveUnread(cfg.GroupContact, 3);
    }

    private void UpdateHint()
    {
        if (finished)
        {
            hintLabel.Visible = false;
            return;
        }

        switch (phase)
        {
            case Phase.Dorm1:
                ShowHint(pinDone ? "hint.ch09_pin_done" : "hint.ch09_phone");
                return;

            case Phase.Lab:
                ShowHint(labDone ? "hint.ch09_lab_done" : "hint.ch09_lab");
                return;

            case Phase.Night2:
                if (readChosen)
                    ShowHint("hint.ch09_night_done");
                else if (night2OpenPlayed)
                    hintLabel.Visible = false; // 「没事」的三种读法正摆在屏幕上
                else
                    ShowHint("hint.ch09_night");
                return;

            case Phase.River:
                ShowHint(planChosen && !popperDone ? "hint.ch09_river_done" : "hint.ch09_river");
                return;

            case Phase.Night3:
                ShowHint("hint.ch09_night3");
                return;
        }

        hintLabel.Visible = false;
    }

    private void ShowHint(string key)
    {
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(key);
    }

    // ==================== 手机 ====================

    /// <summary>
    /// 掏手机。chatId 传空 = 落在锁屏（自己解锁、自己翻进微信）；
    /// 传会话名 = 直接进那个会话。onClose 是"放下手机之后要做的事"。
    /// </summary>
    private void OpenPhone(string chatId, System.Action onClose)
    {
        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
            .Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () =>
        {
            lastActivityMsec = Time.GetTicksMsec();
            onClose?.Invoke();
        };
        overlay.Open(chatId);
    }

    // ==================== 第 1 幕：周一夜，置顶 ====================

    private void OnDormPhonePressed()
    {
        // 第 3 幕（深夜「没事」）有自己的引导和收尾，先分出去
        if (phase == Phase.Night2)
        {
            OnNight2PhonePressed();
            return;
        }

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        Sparkle(phoneBtn);

        // 第 1 幕：先演"点开手机"的引导，再让玩家自己翻进微信去长按置顶
        if (phase == Phase.Dorm1)
        {
            if (!phoneArmed)
            {
                phoneArmed = true;
                UpdateHint(); // 引导要演了，先把"长按它"的提示收掉
                dm.PlaySequence(ChapterId, "open_phone", () => OpenPhone("", EvaluateDorm1));
                return;
            }

            OpenPhone("", EvaluateDorm1);
            return;
        }

        // 第 5 幕：手机是"随便翻"的（置顶的她钉在最上面，五天的记录都在）
        OpenPhone("", null);
    }

    /// <summary>放下手机：她真的被置顶了吗？置顶了才放行去睡觉。</summary>
    private void EvaluateDorm1()
    {
        if (!GodotObject.IsInstanceValid(this) || pinDone)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        if (!GameManager.Instance.IsPinned(cfg.JieContact))
        {
            UpdateHint();
            return;
        }

        pinDone = true;
        hintLabel.Visible = false;
        dm.PlaySequence(ChapterId, "pin_done", () =>
        {
            // pin_intro 里挂着心声微选择（heartChoice），选完分支对白播完才会走到这里
            dm.PlaySequence(ChapterId, "pin_intro", () =>
            {
                bedBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    /// <summary>上铺：这一觉睡过去，就是下一幕</summary>
    private void OnBedPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        switch (phase)
        {
            case Phase.Dorm1 when pinDone:
                bedBtn.Visible = false;
                dm.PlaySequence(ChapterId, "sleep1", StartLab);
                break;

            case Phase.Night2 when readChosen:
                bedBtn.Visible = false;
                StartRiver();
                break;

            case Phase.Night3:
                bedBtn.Visible = false;
                phoneBtn.Visible = false;
                PlayOutro();
                break;
        }
    }

    // ==================== 第 2 幕：周三，实验室 ====================

    private void StartLab()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgLab, 1.1f);
        StoryClock.UseSegment("lab");
        DialogueManager.Instance.PlaySequence(ChapterId, "lab_arrive", EnterLab);
    }

    private void EnterLab()
    {
        phase = Phase.Lab;
        HideAllHotspots();
        flavorDorm.Visible = false;
        flavorLab.Visible = true;
        hotspotsRoot.Visible = true;
        labPhoneBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁周三下午的聊天：她连发三条 + 群里热闹
        RecordGate("lab");
        LiveEvents.GiveUnread(cfg.JieContact, 3);
        LiveEvents.GiveUnread(cfg.GroupContact, 3);
    }

    private void OnLabPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Lab)
            return;

        Sparkle(labPhoneBtn);

        if (!labOpenPlayed)
        {
            labOpenPlayed = true;
            UpdateHint();
            dm.PlaySequence(ChapterId, "lab_open", () => OpenPhone(cfg.JieChat, OnLabChatClosed));
            return;
        }

        OpenPhone(cfg.JieChat, OnLabChatClosed);
    }

    /// <summary>放下手机：三条消息在等，第一句回什么</summary>
    private void OnLabChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || busyChosen)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        busyChosen = true;
        hintLabel.Visible = false;
        dm.ShowChoice(ChapterId, "busy", OnBusyChosen);
    }

    private void OnBusyChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"busy_{index}", () =>
        {
            labDone = true;
            DialogueManager.Instance.PlaySequence(ChapterId, "lab_end", () =>
            {
                labExitBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    private void OnLabExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Lab || !labDone)
            return;

        Sparkle(labExitBtn);
        StartNight2();
    }

    // ==================== 第 3 幕：周四深夜，「没事」 ====================

    private void StartNight2()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        DipTo(bgDorm);
        StoryClock.UseSegment("night2");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -16f, 2.0f); // 夜里私聊那首
        DialogueManager.Instance.PlaySequence(ChapterId, "night2_arrive", EnterNight2);
    }

    private void EnterNight2()
    {
        phase = Phase.Night2;
        HideAllHotspots();
        flavorLab.Visible = false;
        flavorDorm.Visible = true;
        hotspotsRoot.Visible = true;
        phoneBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁周四深夜的聊天：她那句「在吗」「没事」 + 群里约周六
        RecordGate("night2");
        LiveEvents.GiveUnread(cfg.JieContact, 2);
        LiveEvents.GiveUnread(cfg.GroupContact, 2);
    }

    private void OnNight2PhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Night2 || readChosen)
            return;

        Sparkle(phoneBtn);

        if (!night2OpenPlayed)
        {
            night2OpenPlayed = true;
            UpdateHint();
            dm.PlaySequence(ChapterId, "night2_open", () => OpenPhone(cfg.JieChat, OnNight2ChatClosed));
            return;
        }

        OpenPhone(cfg.JieChat, OnNight2ChatClosed);
    }

    /// <summary>放下手机：深夜的「没事」，怎么读？（内心解读：ReadCard）</summary>
    private void OnNight2ChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || readChosen)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        readChosen = true;
        hintLabel.Visible = false;
        dm.ShowChoice(ChapterId, "read", OnReadChosen);
    }

    private void OnReadChosen(int index)
    {
        readIndex = index;
        DialogueManager.Instance.PlaySequence(ChapterId, $"read_{index}", () =>
        {
            // late_intro 里挂着心声微选择
            DialogueManager.Instance.PlaySequence(ChapterId, "late_intro", () =>
            {
                DialogueManager.Instance.PlaySequence(ChapterId, "night2_end", () =>
                {
                    bedBtn.Visible = true;
                    UpdateHint();
                });
            });
        });
    }

    // ==================== 第 4 幕：周六，河堤 ====================

    private void StartRiver()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgRiver, 1.4f);
        StoryClock.UseSegment("riverside");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym2, -15f, 2.5f);
        FadeWind(-14f, 2.2f);
        DialogueManager.Instance.PlaySequence(ChapterId, $"riverside_arrive_{readIndex}", PlayPlanIntro);
    }

    private void PlayPlanIntro()
    {
        phase = Phase.River;
        HideAllHotspots();
        flavorDorm.Visible = false;
        flavorRiver.Visible = true;
        hotspotsRoot.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 这一次，他说了算——三张牌正摊在屏幕上，不用再提示
        hintLabel.Visible = false;
        DialogueManager.Instance.PlaySequence(ChapterId, "plan_intro", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "plan", OnPlanChosen);
        });
    }

    private void OnPlanChosen(int index)
    {
        planChosen = true;
        DialogueManager.Instance.PlaySequence(ChapterId, $"plan_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "riverside_walk", () =>
            {
                bridgeBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    /// <summary>桥洞：风铃在这儿响。兜里那枚礼花筒，该拿出来了</summary>
    private void OnBridgePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || popperDone || !planChosen)
            return;

        popperDone = true;
        Sparkle(bridgeBtn);
        hintLabel.Visible = false;

        dm.PlaySequence(ChapterId, "popper_draw", () =>
        {
            // 拉响！第八章喜糖盒里那枚礼花筒，在这里用掉（背包描述当场换成"用过的"）
            AudioManager.Instance?.PlaySfx(AudioManager.SfxPartyPopper, -6f, 0.05f);
            if (!string.IsNullOrEmpty(cfg.PopperItem))
                GameManager.Instance.MarkItemUsed(cfg.PopperItem);

            DialogueManager.Instance.PlaySequence(ChapterId, "popper_burst", () =>
            {
                // popper_intro 里挂着心声微选择
                DialogueManager.Instance.PlaySequence(ChapterId, "popper_intro", () =>
                {
                    DialogueManager.Instance.PlaySequence(ChapterId, "popper_after", StartNight3);
                });
            });
        });
    }

    // ==================== 第 5 幕：周六夜 ====================

    private void StartNight3()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        DipTo(bgDorm);
        StoryClock.UseSegment("night3");
        FadeWind(-60f, 1.6f); // 回到宿舍，风就留在河堤上了
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -15f, 2.0f);
        DialogueManager.Instance.PlaySequence(ChapterId, "night3_arrive", EnterNight3);
    }

    private void EnterNight3()
    {
        phase = Phase.Night3;
        HideAllHotspots();
        flavorRiver.Visible = false;
        flavorDorm.Visible = true;
        hotspotsRoot.Visible = true;
        phoneBtn.Visible = true;
        bedBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁周六夜的最后两条：「今天很好。」+ 那个很轻的笑脸
        RecordGate("night3");
        LiveEvents.GiveUnread(cfg.JieContact, 1);
    }

    private void PlayOutro()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        DialogueManager.Instance.PlaySequence(ChapterId, "scroll_show", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
            });
        });
    }

    // ==================== 小工具 ====================

    /// <summary>点一下热点：星光 + 记一次"有动静"（发呆计时归零）</summary>
    private void Sparkle(Button btn)
    {
        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(btn);
    }

    /// <summary>把一张背景淡进来（盖在下面那层上面，往前走一幕用）</summary>
    private void CrossfadeTo(TextureRect layer, float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(layer, "modulate:a", 1.0f, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>回宿舍（宿舍在最底下那层）：先把手上那层淡没，再淡进来——中间黑一下，就是"睡了一觉"</summary>
    private void DipTo(TextureRect target)
    {
        var tw = CreateTween();
        foreach (var bg in new[] { bgDorm, bgLab, bgRiver })
        {
            if (bg == target)
                continue;
            if (bg.Modulate.A > 0.01f)
                tw.TweenProperty(bg, "modulate:a", 0.0f, 0.55f);
        }
        tw.TweenProperty(target, "modulate:a", 1.0f, 0.8f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>河堤的风淡入 / 淡出（toDb 高于静音阈值就先开始播）</summary>
    private void FadeWind(float toDb, float seconds)
    {
        if (windPlayer == null)
            return;
        if (toDb > -59f && !windPlayer.Playing)
            windPlayer.Play();
        var tw = CreateTween();
        tw.TweenProperty(windPlayer, "volume_db", toDb, seconds);
    }
}
