using Godot;
using System.Collections.Generic;

/// <summary>
/// 第十四章：爱就梭哈
///
/// 流程（四段，一个傍晚到夜里）：
///   校门   —— 她第一次来他的学校（这次是江洁把 389 公里走完的）→ 手机里两段消息 → 接人
///   校园   —— 三处闲笔（梧桐 / 树下的橘子 / 亮着灯的教学楼），顺序随意，点完才放行
///   小馆   —— 「你算得太细了」「那你算过我们要怎么办吗」→ 心声（supper_heart）→ 她想看实验室
///   实验楼 —— 玻璃门前：手按在手腕上，「你的心跳得好快」→ heartbeat 三选一 → 梭哈 → 结束页
///
/// 【这一章的重心】不是"在一起"这三个字，是"不算了"。
/// 他从第二章起就在算账（里程/电量/车票/论文遍数/配不配），这一章第一次把牌全推出去。
///
/// 【BGM】校门 Warm → 校园 Gym1（主旋律的回声）→ 小馆 PianoBetween → 实验楼 Gym3
///        → 心跳那一刻切回 Gym1 → 梭哈切 Release。音乐跟着"敢不敢"走。
/// </summary>
public partial class Ch14 : ChapterBase
{
    private const string ChapterId = "ch14";

    // ==================== 章节参数（data/chapters/ch14.json） ====================

    private class ChapterConfig
    {
        public string JieContact { get; set; } = "";
        public string JieChat { get; set; } = "";
        public Dictionary<string, string> Gates { get; set; } = new();
    }

    private ChapterConfig cfg = new();

    private void UnlockGate(string key)
    {
        if (cfg.Gates != null && cfg.Gates.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            RecordChoice(v, 0);
    }

    // ==================== 节点 ====================

    private TextureRect bgGate;
    private TextureRect bgCampus;
    private TextureRect bgXiaoguan;
    private TextureRect bgLab;

    private Control hotspotsRoot;
    private Control flavorGate;
    private Control flavorCampus;
    private Control flavorXiaoguan;
    private Control flavorLab;
    private Label hintLabel;

    private Button phoneBtn;
    private Button walkBtn;
    private Button parkBtn;
    private Button standBtn;
    private Button windowBtn;
    private Button supperBtn;
    private Button talkBtn;
    private Button doorBtn;

    private Control phoneGlow;
    private Control walkGlow;
    private Control parkGlow;
    private Control standGlow;
    private Control windowGlow;
    private Control supperGlow;
    private Control talkGlow;
    private Control doorGlow;

    // ==================== 进度 ====================

    private enum Phase { Gate, Campus, Supper, Lab, Done }
    private Phase phase = Phase.Gate;

    private bool phoneDone;          // 校门：手机里的两段消息看过了
    private bool finished;           // 尾声播放中

    /// <summary>校园三处闲笔：看过哪几处（用 id 记，顺序随便）</summary>
    private readonly HashSet<string> campusSeen = new();

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十四章：爱就梭哈 —— 不算里程");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch14.json") ?? new ChapterConfig();

        AudioManager.Instance?.PlayBgm(AudioManager.BgmWarm, -18f, 1.6f);

        bgGate = GetNode<TextureRect>("Background");
        bgCampus = GetNode<TextureRect>("Campus");
        bgXiaoguan = GetNode<TextureRect>("Xiaoguan");
        bgLab = GetNode<TextureRect>("Lab");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        walkBtn = GetNode<Button>("Hotspots/WalkBtn");
        parkBtn = GetNode<Button>("Hotspots/ParkBtn");
        standBtn = GetNode<Button>("Hotspots/StandBtn");
        windowBtn = GetNode<Button>("Hotspots/LabWindowBtn");
        supperBtn = GetNode<Button>("Hotspots/SupperBtn");
        talkBtn = GetNode<Button>("Hotspots/TalkBtn");
        doorBtn = GetNode<Button>("Hotspots/DoorBtn");

        phoneBtn.Pressed += OnPhonePressed;
        walkBtn.Pressed += OnWalkPressed;
        parkBtn.Pressed += () => OnCampusSpot("park", "spot_park", parkBtn);
        standBtn.Pressed += () => OnCampusSpot("stand", "spot_stand", standBtn);
        windowBtn.Pressed += () => OnCampusSpot("window", "spot_lab_window", windowBtn);
        supperBtn.Pressed += OnSupperPressed;
        talkBtn.Pressed += OnTalkPressed;
        doorBtn.Pressed += OnDoorPressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        walkGlow = HotspotGlow.Attach(walkBtn);
        parkGlow = HotspotGlow.Attach(parkBtn);
        standGlow = HotspotGlow.Attach(standBtn);
        windowGlow = HotspotGlow.Attach(windowBtn);
        supperGlow = HotspotGlow.Attach(supperBtn);
        talkGlow = HotspotGlow.Attach(talkBtn);
        doorGlow = HotspotGlow.Attach(doorBtn);

        // 热点位置按"美术图比例"登记（1792 设计稿；1536 的图由 ArtAnchor 自动折算）
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f)); // 兜里的手机
        ArtAnchor.TrackFraction(walkBtn, new Rect2(0.03f, 0.36f, 0.09f, 0.26f));      // 校门里，往里走
        ArtAnchor.TrackFraction(parkBtn, new Rect2(0.12f, 0.42f, 0.12f, 0.22f));      // 梧桐与路灯
        ArtAnchor.TrackFraction(standBtn, new Rect2(0.56f, 0.56f, 0.13f, 0.24f));     // 靠树的那辆自行车
        ArtAnchor.TrackFraction(windowBtn, new Rect2(0.40f, 0.42f, 0.16f, 0.14f));    // 路尽头的图书馆
        ArtAnchor.TrackFraction(supperBtn, new Rect2(0.03f, 0.42f, 0.10f, 0.24f));    // 出校门去吃饭
        ArtAnchor.TrackFraction(talkBtn, new Rect2(0.44f, 0.54f, 0.14f, 0.20f));      // 小馆的圆桌
        ArtAnchor.TrackFraction(doorBtn, new Rect2(0.42f, 0.70f, 0.16f, 0.22f));      // 实验楼玻璃门

        flavorGate = MakeFlavorLayer("ch14_gate");
        flavorCampus = MakeFlavorLayer("ch14_campus");
        flavorXiaoguan = MakeFlavorLayer("ch14_xiaoguan");
        flavorLab = MakeFlavorLayer("ch14_lab");

        UiSounds.WireAll(this);

        // 第 1 段用章节主时刻（3 月 7 日 周六），时段先清干净
        StoryClock.ClearSegment();

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        flavorCampus.Visible = false;
        flavorXiaoguan.Visible = false;
        flavorLab.Visible = false;
        phoneBtn.Visible = true;

        // 她上午和傍晚的两段消息都先摆在那儿——他在门口站了一个小时，才有资格说"刚看到"
        UnlockGate("morning");
        UnlockGate("arrive");
        LiveEvents.GiveUnread(cfg.JieContact, 2);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "gate_wait", OnIntroFinished);
    }

    private Control MakeFlavorLayer(string flavorId)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hotspotsRoot.AddChild(layer);
        hotspotsRoot.MoveChild(layer, 0);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    private void HideAllHotspots()
    {
        foreach (var btn in new[] { phoneBtn, walkBtn, parkBtn, standBtn, windowBtn, supperBtn, talkBtn, doorBtn })
        {
            if (btn != null)
                btn.Visible = false;
        }
    }

    public override void _ExitTree()
    {
        StoryClock.ClearSegment();
        base._ExitTree();
    }

    // ==================== 发呆提示 ====================

    public override void _Process(double delta)
    {
        base._Process(delta);
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

    private List<Control> PendingSpots()
    {
        var list = new List<Control>();
        switch (phase)
        {
            case Phase.Gate:
                if (!phoneDone) Add(list, phoneGlow);
                else Add(list, walkGlow);
                break;

            case Phase.Campus:
                if (!campusSeen.Contains("park")) Add(list, parkGlow);
                if (!campusSeen.Contains("stand")) Add(list, standGlow);
                if (!campusSeen.Contains("window")) Add(list, windowGlow);
                if (campusSeen.Count >= 3) Add(list, supperGlow);
                break;

            case Phase.Supper:
                Add(list, talkGlow);
                break;

            case Phase.Lab:
                Add(list, doorGlow);
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

    private void UpdateHint()
    {
        if (finished)
        {
            hintLabel.Visible = false;
            return;
        }

        switch (phase)
        {
            case Phase.Gate:
                ShowHint(phoneDone ? "hint.ch14_gate_go" : "hint.ch14_phone");
                return;

            case Phase.Campus:
                ShowHint(campusSeen.Count >= 3 ? "hint.ch14_campus_done" : "hint.ch14_campus");
                return;

            case Phase.Supper:
                ShowHint("hint.ch14_supper");
                return;

            case Phase.Lab:
                ShowHint("hint.ch14_lab");
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

    // ==================== 第 1 段：校门 ====================

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        flavorGate.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();
    }

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Gate || phoneDone)
            return;

        Sparkle(phoneBtn);
        phoneDone = true;
        hintLabel.Visible = false;
        OpenPhone(cfg.JieChat, OnGateChatClosed);
    }

    private void OnGateChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || !phoneDone)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        dm.PlaySequence(ChapterId, "gate_meet", () =>
        {
            walkBtn.Visible = true;
            UpdateHint();
        });
    }

    private void OnWalkPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Gate || !phoneDone)
            return;

        Sparkle(walkBtn);
        StartCampus();
    }

    // ==================== 第 2 段：校园（三处闲笔，顺序随意） ====================

    private void StartCampus()
    {
        phase = Phase.Campus;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorGate.Visible = false;
        HideAllHotspots();   // 校门那套热点（手机/往里去）一起收起来，别在校园里留个看不见的按钮

        CrossfadeTo(bgCampus, 1.2f);
        StoryClock.UseSegment("campus");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -17f, 2.2f);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "campus_intro", () =>
        {
            flavorCampus.Visible = true;
            hotspotsRoot.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();
            ShowCampusSpots();
            UpdateHint();
        });
    }

    private void ShowCampusSpots()
    {
        parkBtn.Visible = !campusSeen.Contains("park");
        standBtn.Visible = !campusSeen.Contains("stand");
        windowBtn.Visible = !campusSeen.Contains("window");
        supperBtn.Visible = campusSeen.Count >= 3;
    }

    private void OnCampusSpot(string id, string sequenceId, Button btn)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Campus)
            return;
        if (campusSeen.Contains(id))
            return;

        Sparkle(btn);
        dm.PlaySequence(ChapterId, sequenceId, () =>
        {
            campusSeen.Add(id);
            btn.Visible = false;
            ShowCampusSpots();
            UpdateHint();

            if (campusSeen.Count == 3)
            {
                // 三处都看过：给一点安静的过渡，别让"下一个按钮亮起来"太生硬
                var t = CreateTween();
                t.TweenInterval(1.1f);
                t.TweenCallback(Callable.From(() => { if (!finished) UpdateHint(); }));
            }
        });
    }

    private void OnSupperPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Campus || campusSeen.Count < 3)
            return;

        Sparkle(supperBtn);
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 先把"她说饿了"这句演完，再切场景——不然人已经在包厢里了才开始喊饿
        dm.PlaySequence(ChapterId, "campus_go", StartSupper);
    }

    // ==================== 第 3 段：小馆 ====================

    private void StartSupper()
    {
        phase = Phase.Supper;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorCampus.Visible = false;
        HideAllHotspots();   // 校门的"去吃饭"按钮不能跟着进包厢

        CrossfadeTo(bgXiaoguan, 1.2f);
        StoryClock.UseSegment("supper");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmPianoBetween, -17f, 2.2f);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "supper_arrive", () =>
        {
            // 菜上齐之前的空当：她自己坐着，桌上、窗边随便看；准备好了再跟她开口
            flavorXiaoguan.Visible = true;
            hotspotsRoot.Visible = true;
            talkBtn.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();
            UpdateHint();
        });
    }

    private void OnTalkPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Supper)
            return;

        Sparkle(talkBtn);
        talkBtn.Visible = false;
        flavorXiaoguan.Visible = false;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        dm.PlaySequence(ChapterId, "supper_talk", () =>
        {
            dm.PlaySequence(ChapterId, "supper_heart", () =>
            {
                dm.PlaySequence(ChapterId, "supper_end", StartLab);
            });
        });
    }

    // ==================== 第 4 段：实验楼下 ====================

    private void StartLab()
    {
        phase = Phase.Lab;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorXiaoguan.Visible = false;
        HideAllHotspots();

        CrossfadeTo(bgLab, 1.4f);
        StoryClock.UseSegment("lab");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -17f, 2.5f);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "lab_arrive", () =>
        {
            flavorLab.Visible = true;
            hotspotsRoot.Visible = true;
            doorBtn.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();
            UpdateHint();
        });
    }

    private void OnDoorPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Lab)
            return;

        Sparkle(doorBtn);
        doorBtn.Visible = false;
        flavorLab.Visible = false;
        hintLabel.Visible = false;

        // 心跳那一刻：主旋律回来（序章那首歌，也是她跟着哼跑调的那首）
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -15f, 3.0f);

        dm.PlaySequence(ChapterId, "lab_door", () =>
        {
            dm.ShowChoice(ChapterId, "heartbeat", OnHeartbeatChosen);
        });
    }

    private void OnHeartbeatChosen(int index)
    {
        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, $"beat_{index}", () =>
        {
            AudioManager.Instance?.PlayBgm(AudioManager.BgmRelease, -16f, 3.0f);
            dm.PlaySequence(ChapterId, "allin", () =>
            {
                dm.PlaySequence(ChapterId, "outro", FinishChapter);
            });
        });
    }

    // ==================== 收束 ====================

    private void FinishChapter()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 关系阶段推进：从"刚认识"到"在一起"——结局判定和后续章的"我们算什么"读它
        GameManager.Instance.SetStage(cfg.JieContact, "together");
        GameManager.Instance.RecordRelationshipEvent(cfg.JieContact, "ch14_allin");

        // 2026-10-20：第十五章《那束光》上线，这里不再直接进结束页
        GoToNextChapter("res://scenes/chapters/ch15/ch15.tscn", 15);
    }

    // ==================== 小工具 ====================

    private void Sparkle(Button btn)
    {
        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(btn);
    }

    private void CrossfadeTo(TextureRect layer, float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(layer, "modulate:a", 1.0f, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }
}
