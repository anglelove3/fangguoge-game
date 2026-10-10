using Godot;
using System.Collections.Generic;

/// <summary>
/// 第八章：还想再见
///
/// 流程（六幕，跨六天）：
///   第 1 幕 周一夜·宿舍 —— 群里 47 条消息 + 三条删掉的草稿（ghost typing）→ 发出去哪句（draft）
///   第 2 幕 周二·实验室 —— 小米师妹推名片 → 微信「新的朋友」真的加好友 → 第一句发什么（心声）
///                        → 第一次能真的打字（会话预设回复）
///   第 3 幕 周三夜·宿舍 —— 给她的备注改成什么（remark，落档进存档，第九章《置顶》要用）
///   第 4 幕 周六·婚宴    —— 签到簿 / 婚纱照背景布 / 喜糖盒（隐藏物品：一枚没拉的礼花筒）
///                        → 被起哄"有没有情况"（toast）→ 散席门口的心声
///   第 5 幕 周六夜·宿舍 —— 第二次见面去哪儿（where）
///   第 6 幕 周日晚·老校门 —— 见一瞬。礼花筒没拉 → 试玩结束页
///
/// 【这一章的两个"第一次"】
///   1. 第一次跨天：手机时间用 StoryClock 的"章内时段"跟着剧情走
///      （每一幕进场景时点名要用哪一段，出章自动作废）。
///   2. 第一次让玩家在微信里做一件"真事"：加好友不在剧情脚本里自动完成，
///      而是玩家自己翻到「通讯录 → 新的朋友」按下去。本章脚本只负责"演"和"等"。
///
/// 【人物不写死在代码里】
/// 谁要加好友、群聊叫什么、捡哪件隐藏物品、每一幕震一下的是哪条动态事件、
/// 备注的三个候选文案——全部在 data/chapters/ch08.json。
/// 换人、改文案、调事件顺序都不用碰这个文件。
/// </summary>
public partial class Ch08 : ChapterBase
{
    private const string ChapterId = "ch08";

    // ==================== 章节参数（data/chapters/ch08.json） ====================

    /// <summary>JSON 模型：字段一律写成 public 属性（DataStore 的解析器只认属性）</summary>
    private class ChapterConfig
    {
        public string FriendContact { get; set; } = "";
        public string GroupChat { get; set; } = "";
        public string CrackerItem { get; set; } = "";
        public Dictionary<string, string> LiveEvents { get; set; } = new();
        public List<string> RemarkKeys { get; set; } = new();
    }

    private ChapterConfig cfg = new();

    /// <summary>取某一幕该震的动态事件 id（配置里没写就返回空串）</summary>
    private string EventId(string slot) =>
        cfg.LiveEvents != null && cfg.LiveEvents.TryGetValue(slot, out var id) ? id : "";

    /// <summary>按"幕的名字"放动态事件：配置里没登记这一幕就安静地什么都不做</summary>
    private void FireSlot(string slot)
    {
        var id = EventId(slot);
        if (!string.IsNullOrEmpty(id))
            LiveEvents.Fire(this, ChapterId, id);
    }

    // ==================== 节点 ====================

    private TextureRect bgDorm;
    private TextureRect bgLab;
    private TextureRect bgWedding;
    private TextureRect bgGate;

    private Control hotspotsRoot;
    private Control flavorDorm;
    private Control flavorLab;
    private Control flavorWedding;
    private Label hintLabel;

    // 宿舍（第 1 / 3 / 5 幕共用一间）
    private Button phoneBtn;
    private Button bedBtn;
    // 实验室（第 2 幕）
    private Button labPhoneBtn;
    private Button labExitBtn;
    // 婚宴（第 4 幕）
    private Button wedPhoneBtn;
    private Button signBtn;
    private Button photoBtn;
    private Button candyBtn;
    private Button toastBtn;
    private Button wedExitBtn;

    private Control phoneGlow;
    private Control bedGlow;
    private Control labPhoneGlow;
    private Control labExitGlow;
    private Control signGlow;
    private Control photoGlow;
    private Control candyGlow;
    private Control toastGlow;
    private Control wedExitGlow;

    private AudioStreamPlayer crowdPlayer;

    // ==================== 进度 ====================

    private enum Phase { Dorm1, Lab, Dorm2, Wedding, Dorm3, Gate }
    private Phase phase = Phase.Dorm1;

    private bool draftChosen;      // 第 1 幕：发出去的那句已经选了
    private bool helloPlayed;      // 第 2 幕：第一句发什么（心声）已经演过
    private bool labEndPlayed;     // 第 2 幕：他真的对她开口了，这一幕收尾
    private bool remarkChosen;     // 第 3 幕：备注已落档
    private bool signSeen;         // 第 4 幕：签到簿
    private bool photoSeen;        // 第 4 幕：婚纱照背景布
    private bool candyTaken;       // 第 4 幕：喜糖盒 → 礼花筒到手
    private bool toastChosen;      // 第 4 幕：敬酒那段已经答完
    private bool whereChosen;      // 第 5 幕：去哪儿已经定了
    private bool finished;         // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第八章：还想再见 —— 有些话憋着会发酸");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch08.json") ?? new ChapterConfig();

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym2, -16f, 1.5f);

        bgDorm = GetNode<TextureRect>("Background");
        bgLab = GetNode<TextureRect>("Lab");
        bgWedding = GetNode<TextureRect>("Wedding");
        bgGate = GetNode<TextureRect>("Gate");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        bedBtn = GetNode<Button>("Hotspots/BedBtn");
        labPhoneBtn = GetNode<Button>("Hotspots/LabPhoneBtn");
        labExitBtn = GetNode<Button>("Hotspots/LabExitBtn");
        wedPhoneBtn = GetNode<Button>("Hotspots/WedPhoneBtn");
        signBtn = GetNode<Button>("Hotspots/SignBtn");
        photoBtn = GetNode<Button>("Hotspots/PhotoBtn");
        candyBtn = GetNode<Button>("Hotspots/CandyBtn");
        toastBtn = GetNode<Button>("Hotspots/ToastBtn");
        wedExitBtn = GetNode<Button>("Hotspots/WedExitBtn");

        phoneBtn.Pressed += OnDormPhonePressed;
        bedBtn.Pressed += OnBedPressed;
        labPhoneBtn.Pressed += OnLabPhonePressed;
        labExitBtn.Pressed += OnLabExitPressed;
        wedPhoneBtn.Pressed += OnWedPhonePressed;
        signBtn.Pressed += OnSignPressed;
        photoBtn.Pressed += OnPhotoPressed;
        candyBtn.Pressed += OnCandyPressed;
        toastBtn.Pressed += OnToastPressed;
        wedExitBtn.Pressed += OnWedExitPressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        bedGlow = HotspotGlow.Attach(bedBtn);
        labPhoneGlow = HotspotGlow.Attach(labPhoneBtn);
        labExitGlow = HotspotGlow.Attach(labExitBtn);
        signGlow = HotspotGlow.Attach(signBtn);
        photoGlow = HotspotGlow.Attach(photoBtn);
        candyGlow = HotspotGlow.Attach(candyBtn);
        toastGlow = HotspotGlow.Attach(toastBtn);
        wedExitGlow = HotspotGlow.Attach(wedExitBtn);

        // 热点位置按"美术图比例"登记（背景 cover 缩放时也跟着贴在原处）
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.902f, 0.859f, 0.094f, 0.086f)); // 桌上那台手机
        ArtAnchor.TrackFraction(bedBtn, new Rect2(0.44f, 0.10f, 0.26f, 0.10f));       // 上铺
        ArtAnchor.TrackFraction(labPhoneBtn, new Rect2(0.544f, 0.771f, 0.095f, 0.098f));
        ArtAnchor.TrackFraction(labExitBtn, new Rect2(0.708f, 0.40f, 0.06f, 0.19f));   // 实验室的门
        ArtAnchor.TrackFraction(wedPhoneBtn, new Rect2(0.585f, 0.775f, 0.075f, 0.08f));
        ArtAnchor.TrackFraction(signBtn, new Rect2(0.90f, 0.66f, 0.10f, 0.14f));       // 签到簿
        ArtAnchor.TrackFraction(photoBtn, new Rect2(0.61f, 0.25f, 0.20f, 0.20f));      // 婚纱照背景布
        ArtAnchor.TrackFraction(candyBtn, new Rect2(0.485f, 0.775f, 0.09f, 0.09f));    // 喜糖盘
        ArtAnchor.TrackFraction(toastBtn, new Rect2(0.30f, 0.80f, 0.12f, 0.12f));      // 主桌这一头
        ArtAnchor.TrackFraction(wedExitBtn, new Rect2(0.90f, 0.82f, 0.10f, 0.16f));    // 门口

        // "点万物有回应"：三个场景各一套，跟着幕开关
        flavorDorm = MakeFlavorLayer("ch08_dorm");
        flavorLab = MakeFlavorLayer("ch08_lab");
        flavorWedding = MakeFlavorLayer("ch08_wedding");

        // "手机活起来"：三台手机都挂小红点
        LiveEvents.AttachPhoneDot(phoneBtn);
        LiveEvents.AttachPhoneDot(labPhoneBtn);
        LiveEvents.AttachPhoneDot(wedPhoneBtn);

        // 宴会厅的人声：程序生成的无缝循环，进球宴淡入、散席淡出（沿用麻将声那套写法）
        crowdPlayer = new AudioStreamPlayer { VolumeDb = -60f };
        var crowdStream = GD.Load<AudioStream>(AudioManager.SfxWeddingCrowdLoop);
        if (crowdStream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        crowdPlayer.Stream = crowdStream;
        AddChild(crowdPlayer);

        UiSounds.WireAll(this);

        // 第 1 幕用章节主时刻（10 月 27 日 周一 20:40），时段先清干净
        StoryClock.ClearSegment();

        // 开场只留宿舍这一层：手机（点它看群）先亮着，上铺等草稿选完再出现
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        HideAllHotspots();
        phoneBtn.Visible = true;
        flavorLab.Visible = false;
        flavorWedding.Visible = false;

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
        // 风味层垫到最底下：剧情主热区永远优先接住点击（和第七章同一条规矩）
        hotspotsRoot.MoveChild(layer, 0);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    /// <summary>
    /// 换幕之前先把上一幕的热点全收掉。
    /// 六幕里有三幕在同一间宿舍：漏收一个，就会有一台不属于这个房间的手机浮在图上。
    /// </summary>
    private void HideAllHotspots()
    {
        foreach (var btn in new[]
                 {
                     phoneBtn, bedBtn,
                     labPhoneBtn, labExitBtn,
                     wedPhoneBtn, signBtn, photoBtn, candyBtn, toastBtn, wedExitBtn,
                 })
        {
            if (btn != null)
                btn.Visible = false;
        }
    }

    /// <summary>出了这一章，章内时段作废（下一章不用背着婚宴的日期）</summary>
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
                if (!draftChosen) Add(list, phoneGlow);
                else Add(list, bedGlow);
                break;

            case Phase.Lab:
                Add(list, labPhoneGlow); // 加好友 / 找她说话，都在手机里
                if (labEndPlayed) Add(list, labExitGlow);
                break;

            case Phase.Dorm2:
                Add(list, remarkChosen ? bedGlow : phoneGlow);
                break;

            case Phase.Wedding:
                if (!signSeen) Add(list, signGlow);
                if (!photoSeen) Add(list, photoGlow);
                if (!candyTaken) Add(list, candyGlow);
                if (candyTaken && !toastChosen) Add(list, toastGlow);
                if (toastChosen) Add(list, wedExitGlow);
                break;

            case Phase.Dorm3:
                Add(list, whereChosen ? bedGlow : phoneGlow);
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

        // "手机活起来"：实验室压力第一响（明早组会，谁的样品没跑完）
        FireSlot("dorm1");
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
                ShowHint(draftChosen ? "hint.ch08_sleep1" : "hint.ch08_phone");
                return;

            case Phase.Lab:
                if (labEndPlayed)
                    ShowHint("hint.ch08_lab_done");
                else if (GameManager.Instance.IsFriended(cfg.FriendContact))
                    ShowHint("hint.ch08_jie");
                else
                    ShowHint("hint.ch08_card");
                return;

            case Phase.Dorm2:
                if (remarkChosen)
                    ShowHint("hint.ch08_sleep2");
                else
                    hintLabel.Visible = false; // 备注那三张牌正摊在屏幕上，不用再提示
                return;

            case Phase.Wedding:
                if (toastChosen)
                    ShowHint("hint.ch08_leave");
                else if (candyTaken)
                    ShowHint("hint.ch08_toast");
                else
                    ShowHintArg("hint.ch08_wedding", WeddingSpotsSeen(), 3);
                return;

            case Phase.Dorm3:
                if (whereChosen)
                    ShowHint("hint.ch08_sleep3");
                else
                    hintLabel.Visible = false; // 去哪儿的三张牌正摊在屏幕上
                return;
        }

        hintLabel.Visible = false;
    }

    private void ShowHint(string key)
    {
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(key);
    }

    private void ShowHintArg(string key, params object[] args)
    {
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(key, args);
    }

    private int WeddingSpotsSeen()
    {
        int n = 0;
        if (signSeen) n++;
        if (photoSeen) n++;
        if (candyTaken) n++;
        return n;
    }

    // ==================== 手机（三幕共用一套动作） ====================

    /// <summary>第 1 幕：宿舍的手机 = 「今天的局」。第一次点会先进一段引导，放下手机就该选发哪句了。</summary>
    private void OnDormPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        Sparkle(phoneBtn);

        // 第 3 / 5 幕：手机是"随便翻"的，掏出来落在锁屏
        if (phase != Phase.Dorm1)
        {
            OpenPhone("", null);
            return;
        }

        if (!phoneArmed)
        {
            phoneArmed = true;
            dm.PlaySequence(ChapterId, "open_phone", () => OpenPhone(cfg.GroupChat, OnGroupChatClosed));
            return;
        }

        OpenPhone(cfg.GroupChat, OnGroupChatClosed);
    }

    private bool phoneArmed; // 第 1 幕那段"点开手机"的引导只演一次

    /// <summary>放下「今天的局」：那三条草稿演完了，就轮到玩家决定最后发出去的是哪一句</summary>
    private void OnGroupChatClosed()
    {
        if (!GodotObject.IsInstanceValid(this) || draftChosen)
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        hintLabel.Visible = false;
        dm.ShowChoice(ChapterId, "draft", OnDraftChosen);
    }

    private void OnDraftChosen(int index)
    {
        draftChosen = true;

        DialogueManager.Instance.PlaySequence(ChapterId, $"draft_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "draft_end", () =>
            {
                bedBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    /// <summary>第 2 幕：实验室的手机。第一次点开之前，小米把名片推过来了。</summary>
    private void OnLabPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Lab)
            return;

        Sparkle(labPhoneBtn);

        if (!cardSeen)
        {
            cardSeen = true;
            dm.PlaySequence(ChapterId, "lab_card", () => OpenPhone("", EvaluateLab));
            return;
        }

        OpenPhone("", EvaluateLab);
    }

    private bool cardSeen;

    /// <summary>第 4 幕：婚宴上的手机（申哥那句"店真拆了"会震在这里）</summary>
    private void OnWedPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Wedding)
            return;

        Sparkle(wedPhoneBtn);
        OpenPhone("", null);
    }

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

    /// <summary>
    /// 放下手机之后，看这一幕还差什么。
    /// 加好友是玩家在微信里自己按的（脚本不代劳），这里只负责"演她通过了"和"放行"。
    /// </summary>
    private void EvaluateLab()
    {
        if (!GodotObject.IsInstanceValid(this))
            return;

        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
            return;

        if (!helloPlayed && GameManager.Instance.IsFriended(cfg.FriendContact))
        {
            helloPlayed = true;
            dm.PlaySequence(ChapterId, "jie_hello_intro", EvaluateLab); // 里面挂着心声微选择
            return;
        }

        if (!labEndPlayed && helloPlayed && ChatOverlay.WasQuickReplyUsed(cfg.FriendContact))
        {
            labEndPlayed = true;
            dm.PlaySequence(ChapterId, "lab_end", () =>
            {
                labExitBtn.Visible = true;
                UpdateHint();
            });
            return;
        }

        UpdateHint();
    }

    // ==================== 第 1 / 3 / 5 幕：宿舍的"下一步" ====================

    /// <summary>上铺 / 门口：这一觉睡过去，就是下一幕</summary>
    private void OnBedPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;

        switch (phase)
        {
            case Phase.Dorm1 when draftChosen:
                bedBtn.Visible = false;
                dm.PlaySequence(ChapterId, "sleep1", StartLab);
                break;

            case Phase.Dorm2 when remarkChosen:
                bedBtn.Visible = false;
                StartWedding();
                break;

            case Phase.Dorm3 when whereChosen:
                bedBtn.Visible = false;
                StartGate();
                break;
        }
    }

    // ==================== 第 2 幕：实验室 ====================

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

        // 叶冰玉：周六给你留了桌，不许说忙
        FireSlot("lab");
    }

    private void OnLabExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished)
            return;
        if (phase != Phase.Lab || !labEndPlayed)
            return;

        Sparkle(labExitBtn);
        StartDorm2();
    }

    // ==================== 第 3 幕：周三夜，备注 ====================

    private void StartDorm2()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        DipTo(bgDorm);
        StoryClock.UseSegment("night2");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -16f, 2.0f); // 夜里私聊那首
        DialogueManager.Instance.PlaySequence(ChapterId, "night2_arrive", EnterDorm2);
    }

    private void EnterDorm2()
    {
        phase = Phase.Dorm2;
        HideAllHotspots();
        flavorLab.Visible = false;
        flavorDorm.Visible = true;
        hotspotsRoot.Visible = true;
        phoneBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 备注：改完就落档，微信列表 / 聊天顶栏当场跟着变（第九章《置顶》直接读这个变量）
        DialogueManager.Instance.ShowChoice(ChapterId, "remark", OnRemarkChosen);
    }

    private void OnRemarkChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"remark_{index}", () =>
        {
            string key = cfg.RemarkKeys != null && index >= 0 && index < cfg.RemarkKeys.Count
                ? cfg.RemarkKeys[index]
                : "";
            if (!string.IsNullOrEmpty(key))
                GameManager.Instance.SetRemark(cfg.FriendContact, DataStore.Text(key));

            remarkChosen = true;
            DialogueManager.Instance.PlaySequence(ChapterId, "night2_end", () =>
            {
                bedBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    // ==================== 第 4 幕：婚宴 ====================

    private void StartWedding()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgWedding, 1.3f);
        StoryClock.UseSegment("wedding");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 2.0f); // 婚宴用稍快的那首
        FadeCrowd(-18f, 2.4f);
        DialogueManager.Instance.PlaySequence(ChapterId, "wedding_arrive", EnterWedding);
    }

    private void EnterWedding()
    {
        phase = Phase.Wedding;
        HideAllHotspots();
        flavorDorm.Visible = false;
        flavorWedding.Visible = true;
        hotspotsRoot.Visible = true;
        wedPhoneBtn.Visible = true;
        signBtn.Visible = true;
        photoBtn.Visible = true;
        candyBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 申哥：老校门那家店真拆了（第 6 幕的地点伏笔）
        FireSlot("wedding");
    }

    private void OnSignPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || signSeen)
            return;

        signSeen = true;
        Sparkle(signBtn);
        signBtn.Visible = false;
        UpdateHint();
        dm.PlaySequence(ChapterId, "sign_book");
    }

    private void OnPhotoPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || photoSeen)
            return;

        photoSeen = true;
        Sparkle(photoBtn);
        photoBtn.Visible = false;
        UpdateHint();
        dm.PlaySequence(ChapterId, "photo_cloth");
    }

    /// <summary>喜糖盒 → 叶冰玉塞过来一枚没拉的礼花筒（本章的隐藏物品，第九章的钩子）</summary>
    private void OnCandyPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || candyTaken)
            return;

        candyTaken = true;
        Sparkle(candyBtn);
        candyBtn.Visible = false;
        AudioManager.Instance?.PlaySfx(AudioManager.SfxClink, -10f, 0.05f);
        UpdateHint();

        dm.PlaySequence(ChapterId, "candy_box", () =>
        {
            if (!string.IsNullOrEmpty(cfg.CrackerItem))
                FindHiddenItem(cfg.CrackerItem);
            toastBtn.Visible = true;
            UpdateHint();
        });
    }

    private void OnToastPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || toastChosen || !candyTaken)
            return;

        Sparkle(toastBtn);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxClink, -6f, 0.03f);
        hintLabel.Visible = false;

        dm.PlaySequence(ChapterId, "toast_intro", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "toast", OnToastChosen);
        });
    }

    private void OnToastChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"toast_{index}", () =>
        {
            toastChosen = true;
            toastBtn.Visible = false;
            // 散席：门口那段心声（无数值，只进选择回顾）
            DialogueManager.Instance.PlaySequence(ChapterId, "leave_intro", () =>
            {
                wedExitBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    private void OnWedExitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || !toastChosen)
            return;

        Sparkle(wedExitBtn);
        StartDorm3();
    }

    // ==================== 第 5 幕：周六夜 ====================

    private void StartDorm3()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        DipTo(bgDorm);
        StoryClock.UseSegment("wedding_night");
        FadeCrowd(-60f, 1.6f); // 出了宴会厅，人声就留在门里了
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -16f, 2.0f);
        DialogueManager.Instance.PlaySequence(ChapterId, "night3_arrive", EnterDorm3);
    }

    private void EnterDorm3()
    {
        phase = Phase.Dorm3;
        HideAllHotspots();
        flavorWedding.Visible = false;
        flavorDorm.Visible = true;
        hotspotsRoot.Visible = true;
        phoneBtn.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // 金艮：二十块转你了，打车回来
        FireSlot("dorm3");

        // 第二次见面去哪儿——这条发出去就不改了
        DialogueManager.Instance.ShowChoice(ChapterId, "where", OnWhereChosen);
    }

    private void OnWhereChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"where_{index}", () =>
        {
            whereChosen = true;
            DialogueManager.Instance.PlaySequence(ChapterId, "night3_end", () =>
            {
                bedBtn.Visible = true;
                UpdateHint();
            });
        });
    }

    // ==================== 第 6 幕：周日晚，老校门口 ====================

    private void StartGate()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgGate, 1.4f);
        StoryClock.UseSegment("gate");
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -15f, 2.5f);
        DialogueManager.Instance.PlaySequence(ChapterId, "gate_arrive", PlayOutro);
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
            GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
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
        foreach (var bg in new[] { bgDorm, bgLab, bgWedding })
        {
            if (bg == target)
                continue;
            if (bg.Modulate.A > 0.01f)
                tw.TweenProperty(bg, "modulate:a", 0.0f, 0.55f);
        }
        tw.TweenProperty(target, "modulate:a", 1.0f, 0.8f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>宴会人声淡入 / 淡出（toDb 高于静音阈值就先开始播）</summary>
    private void FadeCrowd(float toDb, float seconds)
    {
        if (crowdPlayer == null)
            return;
        if (toDb > -59f && !crowdPlayer.Playing)
            crowdPlayer.Play();
        var tw = CreateTween();
        tw.TweenProperty(crowdPlayer, "volume_db", toDb, seconds);
    }
}
