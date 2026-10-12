using Godot;
using System.Collections.Generic;

/// <summary>
/// 第十章：向南
///
/// 流程（五幕，一整天 278 公里）：
///   第 1 幕 清晨 5:40 宿舍 —— 手机回她「你真来啊？傻子。」→ 收行李（充电器进包）
///                          → 窗外的车充了一夜电，出发
///   第 2 幕 上午·国道     —— ★骑行段 1（60km）：按住加速 / 松开滑行，路牌可点
///   第 3 幕 中午·路边小店 —— 充电 + 一碗面（心声：跑这么远干嘛去）
///   第 4 幕 下午·国道     —— ★骑行段 2（215km）：株洲出口（妈妈消息）→ 下雨
///                          → ★关键选择：继续冒雨骑（勇气）/ 停进凉亭（好感）→ 汇合
///   第 5 幕 黄昏·临澜城郊 —— 路牌「临澜 3 km」→ 给她报信 → 最后三公里 → 尾声
///
/// 【整章没有她，整章都是她】江洁不出场，只在手机里。电量条、里程表、
/// 路牌，每一格都在替他数"离她还有多远"。
///
/// 【骑行小游戏（RideGame）为叙事让路】
/// 对白框出现时车自动滑行；里程碑点了播一句"路过的话"（不点不卡流程）；
/// 电量见底 → 推车兜底，绝不挡路；充电点 / 剧情点自动停靠接演出。
///
/// 【人物不写死在代码里】聊天剧本、联系人、充电器物品 id、各幕聊天门控
/// 全部在 data/chapters/ch10.json。骑行段参数（里程/充电点/剧情点）属玩法
/// 结构，在 StartRide1 / StartRide2 里。
/// </summary>
public partial class Ch10 : ChapterBase
{
    private const string ChapterId = "ch10";

    // ==================== 章节参数（data/chapters/ch10.json） ====================

    private class ChapterConfig
    {
        public string JieContact { get; set; } = "";
        public string JieChat { get; set; } = "";
        public string GroupContact { get; set; } = "";
        public string GroupChat { get; set; } = "";
        public string MomContact { get; set; } = "";
        public string MomChat { get; set; } = "";
        public string ChargerItem { get; set; } = "";
        public Dictionary<string, string> Gates { get; set; } = new();
    }

    private ChapterConfig cfg = new();

    /// <summary>解锁一幕的聊天幕门：这幕开始时记一次选择，聊天里带这把钥匙的消息从此可见。</summary>
    private void RecordGate(string slot)
    {
        if (cfg.Gates != null && cfg.Gates.TryGetValue(slot, out var key) && !string.IsNullOrEmpty(key))
            RecordChoice(key, 0);
    }

    // ==================== 节点 ====================

    private TextureRect bgDorm;
    private TextureRect bgShop;
    private TextureRect bgDusk;
    private RideGame ride;

    private Control hotspotsRoot;
    private Control flavorDorm;
    private Control flavorShop;
    private Control flavorDusk;
    private Label hintLabel;

    // 宿舍（第 1 幕）
    private Button dormPhoneBtn;
    private Button bagBtn;
    private Button departBtn;
    // 小店（第 3 幕）
    private Button noodleBtn;
    private Button shopExitBtn;
    // 城郊（第 5 幕）
    private Button duskSignBtn;
    private Button duskPhoneBtn;
    private Button duskEndBtn;

    private Control dormPhoneGlow;
    private Control bagGlow;
    private Control departGlow;
    private Control noodleGlow;
    private Control shopExitGlow;
    private Control duskSignGlow;
    private Control duskPhoneGlow;
    private Control duskEndGlow;

    private AudioStreamPlayer motorPlayer; // 电机声：骑行淡入，停靠淡出
    private AudioStreamPlayer rainPlayer;  // 冷雨：rain_start 淡入，雨小/到达淡出

    // ==================== 进度 ====================

    private enum Phase { Dorm1, Road1, Shop, Road2, Outskirts }
    private Phase phase = Phase.Dorm1;

    private bool phoneArmed;    // 第 1 幕："点开手机"的引导只演一次
    private bool replyDone;     // 第 1 幕：回过她那句「你真来啊？傻子。」
    private bool packed;        // 第 1 幕：行李收拾好了（充电器进包）
    private bool noodleDone;    // 第 3 幕：面吃完了（心声选完）
    private bool signDone;      // 第 5 幕：路牌看过了
    private bool duskPhoneDone; // 第 5 幕：报过信了
    private bool finished;      // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十章：向南 —— 三百公里，一格电一格电地骑完");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch10.json") ?? new ChapterConfig();

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -16f, 1.5f);

        bgDorm = GetNode<TextureRect>("Background");
        bgShop = GetNode<TextureRect>("Shop");
        bgDusk = GetNode<TextureRect>("Outskirts");
        ride = GetNode<RideGame>("Ride");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        dormPhoneBtn = GetNode<Button>("Hotspots/DormPhoneBtn");
        bagBtn = GetNode<Button>("Hotspots/BagBtn");
        departBtn = GetNode<Button>("Hotspots/DepartBtn");
        noodleBtn = GetNode<Button>("Hotspots/NoodleBtn");
        shopExitBtn = GetNode<Button>("Hotspots/ShopExitBtn");
        duskSignBtn = GetNode<Button>("Hotspots/DuskSignBtn");
        duskPhoneBtn = GetNode<Button>("Hotspots/DuskPhoneBtn");
        duskEndBtn = GetNode<Button>("Hotspots/DuskEndBtn");

        dormPhoneBtn.Pressed += OnDormPhonePressed;
        bagBtn.Pressed += OnBagPressed;
        departBtn.Pressed += OnDepartPressed;
        noodleBtn.Pressed += OnNoodlePressed;
        shopExitBtn.Pressed += OnShopExitPressed;
        duskSignBtn.Pressed += OnDuskSignPressed;
        duskPhoneBtn.Pressed += OnDuskPhonePressed;
        duskEndBtn.Pressed += OnDuskEndPressed;

        dormPhoneGlow = HotspotGlow.Attach(dormPhoneBtn);
        bagGlow = HotspotGlow.Attach(bagBtn);
        departGlow = HotspotGlow.Attach(departBtn);
        noodleGlow = HotspotGlow.Attach(noodleBtn);
        shopExitGlow = HotspotGlow.Attach(shopExitBtn);
        duskSignGlow = HotspotGlow.Attach(duskSignBtn);
        duskPhoneGlow = HotspotGlow.Attach(duskPhoneBtn);
        duskEndGlow = HotspotGlow.Attach(duskEndBtn);

        // 热点位置按"美术图比例"登记（背景 cover 缩放时也跟着贴在原处）
        ArtAnchor.TrackFraction(dormPhoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f));
        ArtAnchor.TrackFraction(bagBtn, new Rect2(0.58f, 0.56f, 0.28f, 0.26f));
        ArtAnchor.TrackFraction(departBtn, new Rect2(0.05f, 0.10f, 0.29f, 0.55f));
        ArtAnchor.TrackFraction(noodleBtn, new Rect2(0.24f, 0.58f, 0.22f, 0.26f));
        ArtAnchor.TrackFraction(shopExitBtn, new Rect2(0.54f, 0.35f, 0.12f, 0.50f));
        ArtAnchor.TrackFraction(duskSignBtn, new Rect2(0.62f, 0.42f, 0.10f, 0.13f));
        ArtAnchor.TrackFraction(duskPhoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f));
        ArtAnchor.TrackFraction(duskEndBtn, new Rect2(0.40f, 0.70f, 0.22f, 0.28f));

        // "点万物有回应"：三个场景各一套，跟着幕开关
        flavorDorm = MakeFlavorLayer("ch10_dorm");
        flavorShop = MakeFlavorLayer("ch10_shop");
        flavorDusk = MakeFlavorLayer("ch10_dusk");

        // 手机小红点：宿舍和城郊的两台
        LiveEvents.AttachPhoneDot(dormPhoneBtn);
        LiveEvents.AttachPhoneDot(duskPhoneBtn);

        // 电机声 + 冷雨，都是程序生成 / 现成的无缝循环（沿用河堤风声那套写法）
        motorPlayer = MakeLoopPlayer(AudioManager.SfxScooterLoop);
        rainPlayer = MakeLoopPlayer("res://assets/audio/sfx/sfx_rain.wav");

        UiSounds.WireAll(this);

        // 第 1 幕用章节主时刻（1 月 17 日 周六 05:40），时段先清干净
        StoryClock.ClearSegment();

        // 开场只留宿舍这一层：手机先亮着，回复完了行李和门才出现
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        dormPhoneBtn.Visible = true;
        flavorShop.Visible = false;
        flavorDusk.Visible = false;

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
        // 风味层垫到最底下：剧情主热区永远优先接住点击（和第七 / 八 / 九章同一条规矩）
        hotspotsRoot.MoveChild(layer, 0);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    /// <summary>换幕之前先把上一幕的热点全收掉</summary>
    private void HideAllHotspots()
    {
        foreach (var btn in new[] { dormPhoneBtn, bagBtn, departBtn, noodleBtn, shopExitBtn, duskSignBtn, duskPhoneBtn, duskEndBtn })
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

    /// <summary>发呆 12 秒 → 当前这一幕"还没点的地方"轻轻呼吸（骑行段例外：骑就完了）</summary>
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
                if (!replyDone) Add(list, dormPhoneGlow);
                else if (!packed) Add(list, bagGlow);
                else Add(list, departGlow);
                break;

            case Phase.Shop:
                if (!noodleDone) Add(list, noodleGlow);
                else Add(list, shopExitGlow);
                break;

            case Phase.Outskirts:
                if (!signDone) Add(list, duskSignGlow);
                else if (!duskPhoneDone) Add(list, duskPhoneGlow);
                else Add(list, duskEndGlow);
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

        // 解锁昨晚到今早的聊天（铺垫回放 + 她那句「你真来啊？傻子。」+ 群里炸锅）
        RecordGate("dorm1");
        LiveEvents.GiveUnread(cfg.JieContact, 5);
        LiveEvents.GiveUnread(cfg.GroupContact, 4);
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
                if (!replyDone)
                    ShowHint("hint.ch10_phone");
                else if (!packed)
                    ShowHint("hint.ch10_pack");
                else
                    ShowHint("hint.ch10_depart");
                return;

            case Phase.Shop:
                ShowHint(noodleDone ? "hint.ch10_shop_exit" : "hint.ch10_noodle");
                return;

            case Phase.Outskirts:
                if (!signDone)
                    ShowHint("hint.ch10_dusk_sign");
                else if (!duskPhoneDone)
                    ShowHint("hint.ch10_dusk_phone");
                else
                    ShowHint("hint.ch10_dusk_end");
                return;
        }

        // 骑行段（Road1 / Road2）：RideGame 有自己的操作提示
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

    // ==================== 第 1 幕：清晨 5:40，出发前 ====================

    private void OnDormPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        Sparkle(dormPhoneBtn);

        if (!replyDone)
        {
            if (!phoneArmed)
            {
                phoneArmed = true;
                UpdateHint(); // 引导要演了，先把提示收掉
                dm.PlaySequence(ChapterId, "open_phone", () => OpenPhone(cfg.JieChat, OnDormChatClosed));
                return;
            }

            OpenPhone(cfg.JieChat, OnDormChatClosed);
            return;
        }

        // 回过了：手机是"随便翻"的（置顶的她，五天的记录都在）
        OpenPhone("", null);
    }

    /// <summary>放下手机：她那句「你真来啊？傻子。」——回什么？</summary>
    private void OnDormChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || replyDone)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        hintLabel.Visible = false;
        dm.ShowChoice(ChapterId, "reply", OnReplyChosen);
    }

    private void OnReplyChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"reply_{index}", () =>
        {
            replyDone = true;
            bagBtn.Visible = true;
            departBtn.Visible = true;
            UpdateHint();
        });
    }

    /// <summary>书桌：收拾行李。充电器进包——路上每六十公里停一次，全靠它</summary>
    private void OnBagPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || packed)
            return;

        Sparkle(bagBtn);
        hintLabel.Visible = false;
        dm.PlaySequence(ChapterId, "pack", () =>
        {
            packed = true;
            if (!string.IsNullOrEmpty(cfg.ChargerItem))
                GameManager.Instance.AddItem(cfg.ChargerItem);
            bagBtn.Visible = false;
            UpdateHint();
        });
    }

    /// <summary>窗户：楼下的车充了一夜电。出发</summary>
    private void OnDepartPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (!replyDone || !packed)
            return;

        Sparkle(departBtn);
        hintLabel.Visible = false;
        departBtn.Visible = false;
        dm.PlaySequence(ChapterId, "depart", StartRoad1);
    }

    // ==================== 骑行段配置 ====================

    /// <summary>段 1：学校 → 小店，60km（教学段：满电硬冲也够，先学会节奏）</summary>
    private RideGame.RideConfig Seg1Config() => new()
    {
        TotalKm = 60f,
        OdoStart = 278f,
        Milestones = new List<RideGame.MilestoneDef>
        {
            new() { Km = 24f, Id = "fuwu", Label = "青枫服务区" },
            new() { Km = 46f, Id = "shidi", Label = "白鹭湿地" },
        },
    };

    /// <summary>段 2：小店 → 临澜城郊，215km（株洲出口 / 下雨抉择 / 最后一个充电点都在这段）</summary>
    private RideGame.RideConfig Seg2Config() => new()
    {
        TotalKm = 215f,
        OdoStart = 218f,
        Milestones = new List<RideGame.MilestoneDef>
        {
            new() { Km = 25f, Id = "xiangjiang", Label = "湘江大桥" },
            new() { Km = 120f, Id = "boundary", Label = "湘赣界" },
            new() { Km = 185f, Id = "linlan", Label = "临澜 20 km" },
        },
        AutoEvents = new List<RideGame.AutoEventDef>
        {
            new() { Km = 45f, Id = "zhuzhou" },
            new() { Km = 95f, Id = "rain_start" },
            new() { Km = 118f, Id = "rain_choice" },
        },
        ChargeKms = new List<float> { 140f },
    };

    // ==================== 第 2 幕：上午·国道（骑行段 1） ====================

    private void StartRoad1()
    {
        phase = Phase.Road1;
        HideAllHotspots();
        flavorDorm.Visible = false;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        StoryClock.UseSegment("road_am");
        FadeLoop(motorPlayer, -14f, 1.2f);
        DialogueManager.Instance.PlaySequence(ChapterId, "ride1_start", StartRide1);
    }

    private void StartRide1()
    {
        WireRideEvents();
        ride.Start(Seg1Config());
        FadeRideIn();
        lastActivityMsec = Time.GetTicksMsec();
    }

    /// <summary>骑行画面淡入（从宿舍 / 小店切过去，别硬切）</summary>
    private void FadeRideIn()
    {
        ride.Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(ride, "modulate:a", 1.0f, 0.8f);
    }

    // ==================== 第 3 幕：中午·路边小店 ====================

    private void StartShop()
    {
        phase = Phase.Shop;
        StoryClock.UseSegment("shop");

        // 充电器在这儿用掉：背包描述当场换成"用过的"（沿用礼花筒的写法）
        if (!string.IsNullOrEmpty(cfg.ChargerItem))
            GameManager.Instance.MarkItemUsed(cfg.ChargerItem);

        CrossfadeTo(bgShop, 1.1f);
        FadeRideOut(1.1f);
        DialogueManager.Instance.PlaySequence(ChapterId, "shop_arrive", EnterShop);
    }

    /// <summary>骑行画面淡出再收摊（不能瞬间 End——底下是宿舍背景，会闪一帧"回到出发前"）</summary>
    private void FadeRideOut(float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(ride, "modulate:a", 0.0f, seconds);
        tw.TweenCallback(Callable.From(() =>
        {
            ride.End();
            ride.Modulate = new Color(1, 1, 1, 1);
        }));
    }

    private void EnterShop()
    {
        HideAllHotspots();
        flavorDorm.Visible = false;
        flavorShop.Visible = true;
        hotspotsRoot.Visible = true;
        noodleBtn.Visible = true;
        shopExitBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁中午的聊天：她问骑到哪了 + 群里上午的跟踪
        RecordGate("shop");
        LiveEvents.GiveUnread(cfg.JieContact, 6);
        LiveEvents.GiveUnread(cfg.GroupContact, 4);
    }

    private void OnNoodlePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || noodleDone)
            return;

        Sparkle(noodleBtn);
        hintLabel.Visible = false;

        // noodle_intro 里挂着心声微选择（noodles），选完 noodle_after 播完才到这儿
        dm.PlaySequence(ChapterId, "shop_noodle", () =>
        {
            dm.PlaySequence(ChapterId, "noodle_intro", () =>
            {
                noodleDone = true;
                UpdateHint();
            });
        });
    }

    private void OnShopExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Shop || !noodleDone)
            return;

        Sparkle(shopExitBtn);
        hintLabel.Visible = false;
        shopExitBtn.Visible = false;
        noodleBtn.Visible = false;
        dm.PlaySequence(ChapterId, "shop_leave", StartRoad2);
    }

    // ==================== 第 4 幕：下午·国道（骑行段 2） ====================

    private void StartRoad2()
    {
        phase = Phase.Road2;
        HideAllHotspots();
        flavorShop.Visible = false;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        StoryClock.UseSegment("road_pm");
        FadeLoop(motorPlayer, -14f, 1.2f);
        DialogueManager.Instance.PlaySequence(ChapterId, "ride2_start", StartRide2);
    }

    private void StartRide2()
    {
        WireRideEvents();
        ride.Start(Seg2Config());
        FadeRideIn();
        lastActivityMsec = Time.GetTicksMsec();
    }

    /// <summary>骑行事件只在开骑前挂一次（两段共用同一组处理器，按 phase 分流）</summary>
    private bool rideWired;

    private void WireRideEvents()
    {
        if (rideWired)
            return;
        rideWired = true;

        ride.MilestonePressed += OnMilestonePressed;
        ride.ChargeArrived += OnChargeArrived;
        ride.AutoEventReached += OnAutoEventReached;
        ride.BatteryEmpty += OnBatteryEmpty;
        ride.Finished += OnRideFinished;
    }

    /// <summary>点了路牌：停一下，播那句"路过的话"，接着骑</summary>
    private void OnMilestonePressed(string id)
    {
        if (finished)
            return;
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        ride.Pause();
        dm.PlaySequence(ChapterId, $"ms_{id}", () =>
        {
            if (phase is Phase.Road1 or Phase.Road2)
                ride.Resume();
        });
    }

    /// <summary>骑到充电点（RideGame 已停 + 电条回满）：播几句，接着骑</summary>
    private void OnChargeArrived(int index)
    {
        if (finished)
            return;
        var dm = DialogueManager.Instance;
        if (dm == null)
            return;

        dm.PlaySequence(ChapterId, "charge2", () =>
        {
            if (phase is Phase.Road1 or Phase.Road2)
                ride.Resume();
        });
    }

    /// <summary>到剧情点（株洲出口 / 下雨抉择）——RideGame 已自动停</summary>
    private void OnAutoEventReached(string id)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || finished)
            return;

        switch (id)
        {
            case "zhuzhou":
                // 过家门而不入。妈妈的消息在这时解锁（群里的"疯了"跟踪也是）
                RecordGate("road2");
                LiveEvents.GiveUnread(cfg.MomContact, 2);
                LiveEvents.GiveUnread(cfg.GroupContact, 2);
                dm.PlaySequence(ChapterId, "zhuzhou", () => OpenPhone(cfg.MomChat, OnMomClosed));
                break;

            case "rain_start":
                // 下雨了：她 3:50 那几句"你带雨衣了吗"这时候才解锁
                RecordGate("rain");
                StoryClock.UseSegment("rain");
                dm.PlaySequence(ChapterId, "rain_start", () =>
                {
                    ride.SetRain(1f);
                    FadeLoop(rainPlayer, -12f, 1.2f);
                    ride.Resume();
                });
                break;

            case "rain_choice":
                // ★本章戏眼：继续冒雨骑（勇气）/ 停进凉亭（好感）。两条路都汇合进城
                dm.ShowChoice(ChapterId, "rain", OnRainChosen);
                break;
        }
    }

    private void OnMomClosed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null)
            return;
        dm.PlaySequence(ChapterId, "zhuzhou_reply", () =>
        {
            if (phase is Phase.Road1 or Phase.Road2)
                ride.Resume();
        });
    }

    private void OnRainChosen(int index)
    {
        var dm = DialogueManager.Instance;
        dm?.PlaySequence(ChapterId, $"rain_{index}", () =>
        {
            if (index == 1)
            {
                // 停下等雨小的：雨势收成毛毛雾，雨声退远
                ride.SetRain(0.35f);
                FadeLoop(rainPlayer, -26f, 2.5f);
            }
            ride.Resume();
        });
    }

    /// <summary>电量见底（每段最多一次）：推车兜底，播一句，不挡路</summary>
    private void OnBatteryEmpty()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || finished)
            return;
        dm.PlaySequence(ChapterId, "battery_empty");
    }

    /// <summary>骑完一段。段 1 → 小店；段 2 → 临澜城郊</summary>
    private void OnRideFinished()
    {
        if (finished)
            return;

        FadeLoop(motorPlayer, -60f, 1.6f);
        var dm = DialogueManager.Instance;
        if (dm == null)
            return;

        if (phase == Phase.Road1)
        {
            dm.PlaySequence(ChapterId, "ride1_end", StartShop);
        }
        else if (phase == Phase.Road2)
        {
            FadeLoop(rainPlayer, -60f, 2.0f); // 到城郊雨早停了
            dm.PlaySequence(ChapterId, "ride2_end", StartOutskirts);
        }
    }

    // ==================== 第 5 幕：黄昏·临澜城郊 ====================

    private void StartOutskirts()
    {
        phase = Phase.Outskirts;
        StoryClock.UseSegment("dusk");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym2, -15f, 2.5f);
        CrossfadeTo(bgDusk, 1.4f);
        FadeRideOut(1.4f);
        DialogueManager.Instance.PlaySequence(ChapterId, "dusk_arrive", EnterOutskirts);
    }

    private void EnterOutskirts()
    {
        HideAllHotspots();
        flavorShop.Visible = false;
        flavorDusk.Visible = true;
        hotspotsRoot.Visible = true;
        duskSignBtn.Visible = true;
        duskPhoneBtn.Visible = true;
        duskEndBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();
    }

    /// <summary>路牌：「临澜 3 km」</summary>
    private void OnDuskSignPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || signDone)
            return;

        signDone = true;
        Sparkle(duskSignBtn);
        hintLabel.Visible = false;
        dm.PlaySequence(ChapterId, "sign_look", UpdateHint);
    }

    /// <summary>手机：第一下是报信（快到了），之后随便翻（她的回复都在）</summary>
    private void OnDuskPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        Sparkle(duskPhoneBtn);

        if (!duskPhoneDone)
        {
            duskPhoneDone = true;
            hintLabel.Visible = false;
            RecordGate("dusk");
            LiveEvents.GiveUnread(cfg.JieContact, 4);
            dm.PlaySequence(ChapterId, "dusk_phone", UpdateHint);
            return;
        }

        OpenPhone(cfg.JieChat, null);
    }

    /// <summary>骑完最后三公里</summary>
    private void OnDuskEndPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (!signDone || !duskPhoneDone)
            return;

        Sparkle(duskEndBtn);
        hintLabel.Visible = false;
        hotspotsRoot.Visible = false;

        // final3_intro 里挂着心声微选择（final3），选完 final_ride 播完才到这儿
        dm.PlaySequence(ChapterId, "final3_intro", () =>
        {
            dm.PlaySequence(ChapterId, "final_ride", () =>
            {
                dm.PlaySequence(ChapterId, "outro", () =>
                {
                    GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
                });
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

    /// <summary>建一个无缝循环音轨（电机 / 冷雨），音量从静音开始</summary>
    private AudioStreamPlayer MakeLoopPlayer(string path)
    {
        var p = new AudioStreamPlayer { VolumeDb = -60f };
        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        p.Stream = stream;
        AddChild(p);
        return p;
    }

    /// <summary>循环音轨淡入 / 淡出（toDb 高于静音阈值就先开始播）</summary>
    private void FadeLoop(AudioStreamPlayer p, float toDb, float seconds)
    {
        if (p == null)
            return;
        if (toDb > -59f && !p.Playing)
            p.Play();
        var tw = CreateTween();
        tw.TweenProperty(p, "volume_db", toDb, seconds);
    }
}
