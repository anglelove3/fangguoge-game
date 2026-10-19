using Godot;
using System.Collections.Generic;

/// <summary>
/// 第十三章：十里长亭
///
/// 流程（五段，一个傍晚）：
///   老街   —— 按 ch12 的电量选择分流开场（骑到街口 / 推车进城）→ 她的定位 → 出街
///   长堤   —— 「长亭是送别用的，今天这条是迎接的」→ 缓慢推近 → approach 心声 → 路灯下的身影
///   见面   —— firstword：见面第一句话（全书最重的一句，三选一）→ 并肩走
///   小馆   —— 一碗热汤 + 翻相册（一张都没有）+ 导航记录 + 「回去坐高铁」「下次」→ nexttime 心声
///   夜堤   —— 沿堤走回去 → 「一千公里换了意思」→ 尾声 → 结束页
///
/// 【与第十一章的联动】开场形态由 ch12_charge1 的选择决定：稳充线骑到街口才下车；
/// 赌线电量耗尽在坡下、推 5 公里进城——「越接近越慢」的物理版。
///
/// 【BGM】老街 No.1 → 长堤 No.3（慢板）→ 小馆 No.1 → 夜堤 No.3，不再用公路曲——
/// 到了她的城市，轮子的声音就停了。
/// </summary>
public partial class Ch13 : ChapterBase
{
    private const string ChapterId = "ch13";

    // ==================== 章节参数（data/chapters/ch13.json） ====================

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

    private TextureRect bgStreet;
    private TextureRect bgBank;
    private TextureRect bgXiaoguan;

    private Control hotspotsRoot;
    private Control flavorStreet;
    private Control flavorBank;
    private Label hintLabel;

    private Button phoneBtn;
    private Button gateBtn;
    private Button shopExitBtn;

    private Control phoneGlow;
    private Control gateGlow;
    private Control shopExitGlow;

    // ==================== 进度 ====================

    private enum Phase { Street, Walk, Meet, Supper, Night }
    private Phase phase = Phase.Street;

    private int chargeIndex = 0;     // 第十一章的电量选择（0 稳 / 1 赌）——开场分流用
    private bool phoneDone;          // 老街：定位看过了
    private bool finished;           // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十三章：十里长亭 —— 越接近，越慢");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch13.json") ?? new ChapterConfig();

        // 读第十一章的电量选择：骑/推开场分流（没记录时按稳充 0 兜底）
        if (GameManager.Instance.ChoiceHistory.TryGetValue("ch12_charge1", out var saved))
            chargeIndex = saved == 1 ? 1 : 0;

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -18f, 1.5f);

        bgStreet = GetNode<TextureRect>("Background");
        bgBank = GetNode<TextureRect>("Bank");
        bgXiaoguan = GetNode<TextureRect>("Xiaoguan");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        gateBtn = GetNode<Button>("Hotspots/GateBtn");
        shopExitBtn = GetNode<Button>("Hotspots/ShopExitBtn");

        phoneBtn.Pressed += OnPhonePressed;
        gateBtn.Pressed += OnGatePressed;
        shopExitBtn.Pressed += OnShopExitPressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        gateGlow = HotspotGlow.Attach(gateBtn);
        shopExitGlow = HotspotGlow.Attach(shopExitBtn);

        // 热点位置按"美术图比例"登记
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f)); // 兜里的手机
        ArtAnchor.TrackFraction(gateBtn, new Rect2(0.05f, 0.38f, 0.10f, 0.24f));      // 老街路口，往堤去
        ArtAnchor.TrackFraction(shopExitBtn, new Rect2(0.05f, 0.38f, 0.10f, 0.24f));  // 小馆门口

        flavorStreet = MakeFlavorLayer("ch13_street");
        flavorBank = MakeFlavorLayer("ch13_bank");

        UiSounds.WireAll(this);

        // 第 1 段用章节主时刻（1 月 18 日 周日 18:20），时段先清干净
        StoryClock.ClearSegment();

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        phoneBtn.Visible = true;
        flavorBank.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, $"street_arrive_{chargeIndex}", OnIntroFinished);
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
        foreach (var btn in new[] { phoneBtn, gateBtn, shopExitBtn })
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
            case Phase.Street:
                if (!phoneDone) Add(list, phoneGlow);
                else Add(list, gateGlow);
                break;

            case Phase.Supper:
                Add(list, shopExitGlow);
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
            case Phase.Street:
                if (!phoneDone)
                    ShowHint("hint.ch13_phone");
                else
                    ShowHint("hint.ch13_phone_done");
                return;

            case Phase.Walk:
            case Phase.Meet:
                ShowHint("hint.ch13_meet");
                return;

            case Phase.Supper:
                ShowHint("hint.ch13_phone_done");
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

    // ==================== 第 1 段：老街 ====================

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        flavorStreet.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 解锁老街段聊天：她的定位
        UnlockGate("street");
        LiveEvents.GiveUnread(cfg.JieContact, 4);
    }

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Street || phoneDone)
            return;

        Sparkle(phoneBtn);
        phoneDone = true;
        hintLabel.Visible = false;
        OpenPhone(cfg.JieChat, OnStreetChatClosed);
    }

    private void OnStreetChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || !phoneDone)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        dm.PlaySequence(ChapterId, "street_go", () =>
        {
            gateBtn.Visible = true;
            UpdateHint();
        });
    }

    private void OnGatePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Street || !phoneDone)
            return;

        Sparkle(gateBtn);
        StartWalk();
    }

    // ==================== 第 2 段：长堤黄昏（越接近越慢） ====================

    private void StartWalk()
    {
        phase = Phase.Walk;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorStreet.Visible = false;

        // 长堤淡入 + 20 秒缓慢推近——「越接近越慢」的镜头语言
        CrossfadeTo(bgBank, 1.4f);
        bgBank.PivotOffset = new Vector2(896, 512);
        bgBank.Scale = Vector2.One;
        var zoom = CreateTween();
        zoom.TweenProperty(bgBank, "scale", new Vector2(1.12f, 1.12f), 20f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        StoryClock.UseSegment("walk");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -16f, 2.5f);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "walk_arrive", () =>
        {
            flavorBank.Visible = true;
            hotspotsRoot.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();

            // approach 心声（挂在 walk_arrive 之后由序列接续：approach → walk_end）
            dm.PlaySequence(ChapterId, "approach", () =>
            {
                dm.PlaySequence(ChapterId, "walk_end", StartMeet);
            });
        });
    }

    // ==================== 第 3 段：见面 ====================

    private void StartMeet()
    {
        phase = Phase.Meet;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        StoryClock.UseSegment("meet");

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "meet_intro", () =>
        {
            dm.ShowChoice(ChapterId, "firstword", OnFirstWordChosen);
        });
    }

    private void OnFirstWordChosen(int index)
    {
        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, $"firstword_{index}", () =>
        {
            dm.PlaySequence(ChapterId, "meet_end", StartSupper);
        });
    }

    // ==================== 第 4 段：小馆（一碗热汤） ====================

    private void StartSupper()
    {
        phase = Phase.Supper;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        CrossfadeTo(bgXiaoguan, 1.2f);
        StoryClock.UseSegment("night");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -16f, 2.0f);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "supper_arrive", () =>
        {
            // nexttime 心声（挂在 supper_arrive 之后由序列接续：nexttime → supper_end）
            dm.PlaySequence(ChapterId, "nexttime", () =>
            {
                dm.PlaySequence(ChapterId, "supper_end", () =>
                {
                    hotspotsRoot.Visible = true;
                    ShopExitBtnVisible();
                    lastActivityMsec = Time.GetTicksMsec();
                    UpdateHint();
                });
            });
        });
    }

    private void ShopExitBtnVisible()
    {
        shopExitBtn.Visible = true;
    }

    private void OnShopExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Supper)
            return;

        Sparkle(shopExitBtn);
        StartNight();
    }

    // ==================== 第 5 段：夜堤收束 ====================

    private void StartNight()
    {
        phase = Phase.Night;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 回到长堤：夜色调（画面压暗偏蓝，Modulate 直调）
        CrossfadeTo(bgBank, 1.4f);
        bgBank.Modulate = new Color(0.78f, 0.80f, 0.95f, 1f);
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -15f, 2.5f);

        // 解锁夜段聊天
        UnlockGate("night");
        LiveEvents.GiveUnread(cfg.JieContact, 5);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "night_arrive", PlayOutro);
    }

    private void flavorXiaoguanOff()
    {
        // 小馆层退场（保留 Bank 层的 flavor 状态）
    }

    private void PlayOutro()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        // 第三轮：走之前，最后一次可以回头（或不回头）——两种收尾
        DialogueManager.Instance.ShowChoice(ChapterId, "lookback", OnLookBackChosen);
    }

    private void OnLookBackChosen(int index)
    {
        string seq = index == 0 ? "lookback_yes" : "lookback_no";
        DialogueManager.Instance.PlaySequence(ChapterId, seq, PlayEndingOutro);
    }

    private void PlayEndingOutro()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "outro", PlayEpilogueVariant);
    }

    /// <summary>
    /// 第三轮：结局按好感分三版尾声——同一场告别，往后的日子不一样。
    /// 高好感：她主动了（他终于接住）；低好感：谁都没再开口（尊重沉默）。
    /// </summary>
    private void PlayEpilogueVariant()
    {
        int aff = GameManager.Instance.Affection;
        string epi = aff >= 62 ? "epilogue_warm" : (aff < 40 ? "epilogue_cold" : "");
        if (string.IsNullOrEmpty(epi))
        {
            FinishEnding();
            return;
        }
        DialogueManager.Instance.PlaySequence(ChapterId, epi, FinishEnding);
    }

    private void FinishEnding()
    {
        var bg = GetNodeOrNull<TextureRect>("Background");
        if (bg != null)
        {
            bg.PivotOffset = GetViewportRect().Size / 2f;
            var drift = CreateTween();
            drift.SetParallel(true);
            drift.TweenProperty(bg, "position:x", bg.Position.X - 90f, 20.0)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            drift.TweenProperty(bg, "scale", new Vector2(1.045f, 1.045f), 20.0)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        }
        // 2026-10-19：第十四章《爱就梭哈》上线，这里不再直接进结束页
        GoToNextChapter("res://scenes/chapters/ch14/ch14.tscn", 14);
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
