using Godot;
using System.Collections.Generic;

/// <summary>
/// 第十章：向南
///
/// 流程（五幕，一天 + 前夜）：
///   第 1 幕 周六夜·宿舍 —— 她那句「出门全靠走」→ why 三选（告知 / 套话 / 保密）
///                          → 收拾行装（充电器 / 雨衣）→ 心声 → 睡
///   第 2 幕 周日清晨·国道 —— RideTrack 滚行启动 → 早点摊一碗面 → 电量节点（charge1：稳 / 赌）
///   第 3 幕 中午·充电小店 —— 老板、火腿肠、她的消息（按 why 线分流）→ 心声 → 出店
///   第 4 幕 下午·雨 —— rain 三选（继续骑 / 桥洞等 / 先问她）→ 雨停 → 第三次充电（按 charge1 分流）
///   第 5 幕 黄昏·临江 —— 路牌掠过 → 电量结算（A 骑到街口 / B 推 5km 坡道）→「我到了」→ 尾声
///
/// 【两个机制】
///   1. 全程三线分层：why 的选择决定她"知不知道他要来"，之后每一段聊天消息
///      都按 ch10_gate_{段落}_{线} 三份互斥钥匙放行——选哪条线，全程只看得见哪条线。
///   2. 电量跨章联动：charge1 的选择一路连锁到第三次充电（B 线排队只充到 80%），
///      最终决定黄昏"骑到街口"还是"推着车走完最后 5 公里"——联动到第十一章开场。
/// </summary>
public partial class Ch10 : ChapterBase
{
    private const string ChapterId = "ch10";

    // ==================== 章节参数（data/chapters/ch10.json） ====================

    private class ChapterConfig
    {
        public string JieContact { get; set; } = "";
        public string JieChat { get; set; } = "";
        public string ScooterSfx { get; set; } = "";
        public string GatePrefix { get; set; } = "ch10_gate_";
        public Dictionary<string, string> Gates { get; set; } = new();
    }

    private ChapterConfig cfg = new();

    /// <summary>why 选择 0/1/2 → 线字符 a/b/c（聊天分层钥匙的一部分）</summary>
    private static string LineChar(int whyIndex) => "abc"[Mathf.Clamp(whyIndex, 0, 2)].ToString();

    /// <summary>解锁一把"段落+线"组合钥匙：这一段的聊天里，只有玩家选中的那条线会渲染</summary>
    private void UnlockLineGate(string seg)
    {
        RecordChoice($"{cfg.GatePrefix}{seg}_{LineChar(whyIndex)}", 0);
    }

    /// <summary>解锁共通段钥匙（eve 段：她的到家消息不分线）</summary>
    private void UnlockGate(string key)
    {
        if (cfg.Gates != null && cfg.Gates.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            RecordChoice(v, 0);
    }

    // ==================== 节点 ====================

    private TextureRect bgDorm;
    private TextureRect bgShop;
    private RideTrack ride;

    private Control hotspotsRoot;
    private Control flavorDorm;
    private Control flavorShop;
    private Label hintLabel;

    private Button phoneBtn;
    private Button bedBtn;
    private Button chargerBtn;
    private Button raincoatBtn;
    private Button shopPhoneBtn;
    private Button shopExitBtn;

    private Control phoneGlow;
    private Control bedGlow;
    private Control chargerGlow;
    private Control raincoatGlow;
    private Control shopPhoneGlow;
    private Control shopExitGlow;

    // ==================== 进度 ====================

    private enum Phase { Eve, Road1, Shop, Road2, Road3 }
    private Phase phase = Phase.Eve;

    private int whyIndex = -1;       // 第 1 幕：她知不知道（0 告知 / 1 套话 / 2 保密）
    private int packed;              // 第 1 幕：行装点了几件（0~2）
    private int chargeIndex = -1;    // 第 2 幕：电量选择（0 稳 / 1 赌）
    private bool shopMsgDone;        // 第 3 幕：她的消息看过了
    private int rainIndex = -1;      // 第 4 幕：雨的选择
    private bool finished;           // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十章：向南 —— 389 公里");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch10.json") ?? new ChapterConfig();

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -16f, 1.5f);

        bgDorm = GetNode<TextureRect>("Background");
        bgShop = GetNode<TextureRect>("ShopLayer");
        ride = GetNode<RideTrack>("RideTrack");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        bedBtn = GetNode<Button>("Hotspots/BedBtn");
        chargerBtn = GetNode<Button>("Hotspots/ChargerBtn");
        raincoatBtn = GetNode<Button>("Hotspots/RaincoatBtn");
        shopPhoneBtn = GetNode<Button>("Hotspots/ShopPhoneBtn");
        shopExitBtn = GetNode<Button>("Hotspots/ShopExitBtn");

        phoneBtn.Pressed += OnPhonePressed;
        bedBtn.Pressed += OnBedPressed;
        chargerBtn.Pressed += OnChargerPressed;
        raincoatBtn.Pressed += OnRaincoatPressed;
        shopPhoneBtn.Pressed += OnShopPhonePressed;
        shopExitBtn.Pressed += OnShopExitPressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        bedGlow = HotspotGlow.Attach(bedBtn);
        chargerGlow = HotspotGlow.Attach(chargerBtn);
        raincoatGlow = HotspotGlow.Attach(raincoatBtn);
        shopPhoneGlow = HotspotGlow.Attach(shopPhoneBtn);
        shopExitGlow = HotspotGlow.Attach(shopExitBtn);

        // 热点位置按"美术图比例"登记
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f)); // 桌上手机
        ArtAnchor.TrackFraction(bedBtn, new Rect2(0.44f, 0.10f, 0.26f, 0.10f));       // 上铺
        ArtAnchor.TrackFraction(chargerBtn, new Rect2(0.06f, 0.58f, 0.14f, 0.10f));   // 桌上的充电器
        ArtAnchor.TrackFraction(raincoatBtn, new Rect2(0.08f, 0.75f, 0.14f, 0.12f));  // 门后的雨衣
        ArtAnchor.TrackFraction(shopPhoneBtn, new Rect2(0.544f, 0.771f, 0.095f, 0.098f));
        ArtAnchor.TrackFraction(shopExitBtn, new Rect2(0.05f, 0.40f, 0.10f, 0.24f));  // 店门口的路

        // "点万物有回应"：宿舍 / 小店各一层
        flavorDorm = MakeFlavorLayer("ch10_dorm");
        flavorShop = MakeFlavorLayer("ch10_shop");

        // 骑行层：底图 + 电机声先备好（层默认隐藏）
        ride.SetBackground(GD.Load<Texture2D>("res://assets/art/backgrounds/road_v1.png"));
        if (!string.IsNullOrEmpty(cfg.ScooterSfx))
            ride.SetEngineStream(GD.Load<AudioStream>(cfg.ScooterSfx));

        UiSounds.WireAll(this);

        // 第 1 幕用章节主时刻（1 月 17 日 周六 21:40）
        StoryClock.ClearSegment();

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        phoneBtn.Visible = true;
        flavorShop.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
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
        foreach (var btn in new[] { phoneBtn, bedBtn, chargerBtn, raincoatBtn, shopPhoneBtn, shopExitBtn })
        {
            if (btn != null)
                btn.Visible = false;
        }
    }

    public override void _ExitTree()
    {
        StoryClock.ClearSegment();
    }

    // ==================== 发呆提示 ====================

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

    private List<Control> PendingSpots()
    {
        var list = new List<Control>();
        switch (phase)
        {
            case Phase.Eve:
                if (whyIndex < 0) break; // why 选项正摆在屏幕上
                if (packed < 2)
                {
                    Add(list, chargerGlow);
                    Add(list, raincoatGlow);
                }
                else
                    Add(list, bedGlow);
                break;

            case Phase.Shop:
                if (!shopMsgDone) Add(list, shopPhoneGlow);
                else Add(list, shopExitGlow);
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
        lastActivityMsec = Time.GetTicksMsec();

        // 她到家的消息解锁（共通段），小红点亮起来
        UnlockGate("eve");
        LiveEvents.GiveUnread(cfg.JieContact, 2);

        // 为什么去 / 怎么回——第十章的第一个选择，直接摊开
        DialogueManager.Instance.ShowChoice(ChapterId, "why", OnWhyChosen);
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
            case Phase.Eve:
                if (whyIndex < 0)
                    ShowHint("hint.ch10_pack");
                else if (packed < 2)
                    ShowHint("hint.ch10_pack");
                else
                    ShowHint("hint.ch10_pack_done");
                return;

            case Phase.Shop:
                ShowHint(shopMsgDone ? "hint.ch10_shop_done" : "hint.ch10_shop");
                return;

            case Phase.Road1:
            case Phase.Road2:
            case Phase.Road3:
                hintLabel.Visible = false; // 路上不提示——路牌就是提示
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

    // ==================== 第 1 幕：周六夜，决定 ====================

    /// <summary>宿舍的手机是"随便翻"的：她到家的消息已经解锁，翻完放回来就行</summary>
    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        Sparkle(phoneBtn);
        OpenPhone("", null);
    }

    private void OnWhyChosen(int index)
    {
        whyIndex = index;
        DialogueManager.Instance.PlaySequence(ChapterId, $"why_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "pack_intro", () =>
            {
                chargerBtn.Visible = true;
                raincoatBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    private void OnChargerPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || chargerBtn.Visible == false)
            return;

        Sparkle(chargerBtn);
        if (chargerBtn.HasMeta("packed"))
            return;
        chargerBtn.SetMeta("packed", 1);
        packed++;
        hintLabel.Visible = false;
        dm.PlaySequence(ChapterId, "pack_charger", PackedStep);
    }

    private void OnRaincoatPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || raincoatBtn.Visible == false)
            return;

        Sparkle(raincoatBtn);
        if (raincoatBtn.HasMeta("packed"))
            return;
        raincoatBtn.SetMeta("packed", 1);
        packed++;
        hintLabel.Visible = false;
        dm.PlaySequence(ChapterId, "pack_raincoat", PackedStep);
    }

    private void PackedStep()
    {
        if (packed >= 2)
        {
            bedBtn.Visible = true;
            UpdateHint();
        }
        else
        {
            lastActivityMsec = Time.GetTicksMsec();
        }
    }

    private void OnBedPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Eve || whyIndex < 0 || packed < 2)
            return;

        bedBtn.Visible = false;
        chargerBtn.Visible = false;
        raincoatBtn.Visible = false;

        // decide 心声挂在 decide 序列里（heartChoice）
        dm.PlaySequence(ChapterId, "decide", () =>
        {
            dm.PlaySequence(ChapterId, "sleep", StartRoad1);
        });
    }

    // ==================== 第 2 幕：清晨，国道 ====================

    private void StartRoad1()
    {
        phase = Phase.Road1;
        hotspotsRoot.Visible = false;
        flavorDorm.Visible = false;
        hintLabel.Visible = false;

        // 骑行层上场：清晨天光 + 出发（公路 BGM 接班，宿舍那首 Gymnopédie 交出去）
        bgDorm.Modulate = new Color(1, 1, 1, 0);
        ride.Visible = true;
        ride.SetTint("morning");
        ride.Start();
        ride.AddSign("G205 往南", 1.2f);
        AudioManager.Instance?.PlayBgm(AudioManager.BgmRoad, -13f, 2.5f);

        StoryClock.UseSegment("road_morning");

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "road1_arrive", () =>
        {
            // 清晨段的聊天解锁（她的叮嘱 / 起哄 / 日常——按线）
            UnlockLineGate("road1");
            LiveEvents.GiveUnread(cfg.JieContact, 2);

            dm.PlaySequence(ChapterId, "road1_ride", () =>
            {
                ride.SetBattery(58f);
                ride.PauseForChoice();
                dm.ShowChoice(ChapterId, "charge1", OnCharge1Chosen);
            });
        });
    }

    private void OnCharge1Chosen(int index)
    {
        chargeIndex = index;
        var dm = DialogueManager.Instance;

        // B 线（赌）途中电量报警演出
        if (index == 1)
            ride.SetBattery(10f, "电量低");

        dm.PlaySequence(ChapterId, $"charge1_{index}", () =>
        {
            ride.Resume();
            ride.SetBattery(index == 0 ? 100f : 18f);

            dm.PlaySequence(ChapterId, "road1_end", StartShop);
        });
    }

    // ==================== 第 3 幕：中午，充电小店 ====================

    private void StartShop()
    {
        phase = Phase.Shop;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 停车进店：滚行层让位，小店背景淡入
        ride.Stop();
        ride.HideBattery();
        ride.Visible = false;
        CrossfadeTo(bgShop, 1.0f);

        StoryClock.UseSegment("road_noon");

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, $"shop_arrive_{chargeIndex}", () =>
        {
            hotspotsRoot.Visible = true;
            flavorShop.Visible = true;
            shopPhoneBtn.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();

            // 中午段的聊天解锁
            UnlockLineGate("shop");
            LiveEvents.GiveUnread(cfg.JieContact, 3);

            UpdateHint();
        });
    }

    private void OnShopPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Shop || shopMsgDone)
            return;

        Sparkle(shopPhoneBtn);
        shopMsgDone = true;
        hintLabel.Visible = false;
        OpenPhone(cfg.JieChat, OnShopChatClosed);
    }

    private void OnShopChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || !shopMsgDone)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        // 她的消息按 why 线在聊天里看过了；这里补他的反应 + 心声
        dm.PlaySequence(ChapterId, $"shop_msg_{whyIndex}", () =>
        {
            dm.PlaySequence(ChapterId, "noodle", () =>
            {
                dm.PlaySequence(ChapterId, "shop_end", () =>
                {
                    shopExitBtn.Visible = true;
                    UpdateHint();
                });
            });
        });
    }

    private void OnShopExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Shop || !shopMsgDone)
            return;

        Sparkle(shopExitBtn);
        StartRoad2();
    }

    // ==================== 第 4 幕：下午，雨 ====================

    private void StartRoad2()
    {
        phase = Phase.Road2;
        hotspotsRoot.Visible = false;
        flavorShop.Visible = false;
        hintLabel.Visible = false;

        // 回到公路：雨来
        bgShop.Modulate = new Color(1, 1, 1, 0);
        ride.Visible = true;
        ride.SetTint("rain");
        ride.SetRain(true);
        ride.SetBattery(chargeIndex == 0 ? 88f : 62f);
        ride.Start();

        StoryClock.UseSegment("road_rain");

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "rain_arrive", () =>
        {
            UnlockLineGate("rain");
            LiveEvents.GiveUnread(cfg.JieContact, 2);

            ride.PauseForChoice();
            dm.ShowChoice(ChapterId, "rain", OnRainChosen);
        });
    }

    private void OnRainChosen(int index)
    {
        rainIndex = index;
        var dm = DialogueManager.Instance;

        dm.PlaySequence(ChapterId, $"rain_{index}", () =>
        {
            // 雨停：第三次充电（按 charge1 分流：A 桩空即充 / B 排队只到 80%）
            ride.Resume();
            ride.SetRain(false);
            ride.SetTint("noon");

            dm.PlaySequence(ChapterId, "rain_end", () =>
            {
                ride.PauseForChoice();
                dm.PlaySequence(ChapterId, $"charge3_{chargeIndex}", () =>
                {
                    ride.Resume();
                    ride.SetBattery(chargeIndex == 0 ? 100f : 80f);

                    // burn 心声：雨停了，天边烧起来
                    dm.PlaySequence(ChapterId, "burn", StartRoad3);
                });
            });
        });
    }

    // ==================== 第 5 幕：黄昏，临江 ====================

    private void StartRoad3()
    {
        phase = Phase.Road3;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        ride.SetTint("dusk");
        StoryClock.UseSegment("road_dusk");

        var dm = DialogueManager.Instance;

        // 黄昏段的聊天解锁 + 到达路牌
        UnlockLineGate("dusk");
        LiveEvents.GiveUnread(cfg.JieContact, 2);

        ride.AddSign("临江 20km", 0.8f, 1.1f);
        ride.AddSign("临江 5km", 4.5f, 1.0f);

        dm.PlaySequence(ChapterId, "dusk_arrive", () =>
        {
            // 电量结算：A 骑到街口 / B 推车 5km
            if (chargeIndex == 1)
                ride.SetBattery(8f, "电量耗尽");

            dm.PlaySequence(ChapterId, $"empty_{chargeIndex}", () =>
            {
                // 到达段（按 why 线分流：A 她在路口等 / B「糖水铺怎么查到我们市」/ C 纯炸弹）
                UnlockLineGate("arrive");
                LiveEvents.GiveUnread(cfg.JieContact, 4);

                ride.Stop(2.0f);
                ride.HideBattery();

                dm.PlaySequence(ChapterId, "arrive_intro", () =>
                {
                    dm.PlaySequence(ChapterId, $"arrive_{whyIndex}", () =>
                    {
                        dm.PlaySequence(ChapterId, "dusk_end", PlayOutro);
                    });
                });
            });
        });
    }

    private void PlayOutro()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
        {
            // r20：第十章尾声改接第十一章《十里长亭》（demo_end 改由第十一章尾声进入）
            GoToNextChapter("res://scenes/chapters/ch11/ch11.tscn", 11);
        });
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
