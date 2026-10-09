using Godot;

/// <summary>
/// 第七章：三点水
///
/// 流程：周六傍晚的宿舍（群里在攒局「今天的局」）
///      → 收起手机出门 → 罗曼蒂克（台球房：第一次见到江洁）
///      → 上场打两杆（心声微选择 + 台球桌演出：白球送杆、彩球拐弯进袋）
///      → 皖江宴包厢（选座位：挨着她坐 or 靠门坐）
///      → 开席（师妹桌子底下递话的心声微选择）→ 三选一：第一段话说什么
///      → 散场的尾声（三点水三个字落地）→ 试玩结束页
///
/// 【本章演出核心】
///   - 台球桌是"活"的：BilliardShot.cs 纯代码画的球，白球能滑出去、彩球能拐弯进袋。
///   - 两个场所的换场都是背景交叉淡入淡出 + 麻将声淡入淡出。
///   - 选座位不是选项框，是直接点包厢里那两张空椅子。
///   - 手机全程能开：打台球的时候，叶冰玉的婚礼请帖会悄悄到。
/// </summary>
public partial class Ch07 : ChapterBase
{
    private const string ChapterId = "ch07";
    private const string GroupChatFile = "ch07_plan"; // 对应 data/chat/ch07_plan.json

    private TextureRect bgDorm;
    private TextureRect bgRmt;
    private TextureRect bgWjy;

    private Control hotspotsRoot;
    private Control flavorDorm;
    private Control flavorRmt;
    private Control flavorWjy;
    private Label hintLabel;

    private Button phoneBtn;
    private Button tableBtn;
    private Button seatNearBtn;
    private Button seatFarBtn;

    private Control phoneGlow;
    private Control tableGlow;
    private Control seatNearGlow;
    private Control seatFarGlow;

    private BilliardShot shot;
    private AudioStreamPlayer mahjongPlayer;

    private enum Phase { Dorm, Rmt, Wjy }
    private Phase phase = Phase.Dorm;

    private bool phoneSeenOnce;   // 手机第一次看过（开场的引导走完）
    private bool chatSeenBefore;  // 群聊打开过（之后的打开走微信主页）
    private bool chatOpen;        // 手机界面是否开着
    private bool rmtStarted;      // 已经出门去罗曼蒂克
    private bool tableStarted;    // 台球演出已经开始
    private bool cueDone;         // 第一杆打完了
    private bool seated;          // 座位已经选过
    private bool finished;        // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第七章：三点水 —— 朋友攒的局，和一个名字里有水的姑娘");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 1.5f);

        bgDorm = GetNode<TextureRect>("Background");
        bgRmt = GetNode<TextureRect>("Luoman");
        bgWjy = GetNode<TextureRect>("Wanjiang");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        tableBtn = GetNode<Button>("Hotspots/TableBtn");
        seatNearBtn = GetNode<Button>("Hotspots/SeatNearBtn");
        seatFarBtn = GetNode<Button>("Hotspots/SeatFarBtn");

        phoneBtn.Pressed += OnPhonePressed;
        tableBtn.Pressed += OnTablePressed;
        seatNearBtn.Pressed += () => OnSeatPressed(0);
        seatFarBtn.Pressed += () => OnSeatPressed(1);

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        tableGlow = HotspotGlow.Attach(tableBtn);
        seatNearGlow = HotspotGlow.Attach(seatNearBtn);
        seatFarGlow = HotspotGlow.Attach(seatFarBtn);

        // "点万物有回应"：三个场景各一套，跟着阶段开关
        flavorDorm = MakeFlavorLayer("ch07_dorm");
        flavorRmt = MakeFlavorLayer("ch07_rmt");
        flavorWjy = MakeFlavorLayer("ch07_wjy");

        // "手机活起来"：手机热点右上角挂小红点
        LiveEvents.AttachPhoneDot(phoneBtn);

        // 麻将声：程序生成的 7 秒无缝循环，进罗曼蒂克时淡入、出门时淡出
        mahjongPlayer = new AudioStreamPlayer { VolumeDb = -60f };
        var mahjongStream = GD.Load<AudioStream>(AudioManager.SfxMahjongLoop);
        if (mahjongStream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        mahjongPlayer.Stream = mahjongStream;
        AddChild(mahjongPlayer);

        // 台球桌演出（纯代码绘制）
        shot = GetNode<BilliardShot>("Shot");

        UiSounds.WireAll(this);

        // 开场：只有宿舍阶段能看
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        tableBtn.Visible = false;
        seatNearBtn.Visible = false;
        seatFarBtn.Visible = false;
        flavorRmt.Visible = false;
        flavorWjy.Visible = false;
        // 台球演出层只在球房阶段露头：它自绘的白球本来要"焊"在球房背景那颗静止白球上，
        // 出现在宿舍/包厢里就会变成一个浮在画上的小白点（r14 回归截图抓到）
        shot.Visible = false;

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
        // 风味层垫到最底下：剧情主热区（台球桌、椅子……）永远优先接住点击。
        // 不垫的话，后加的"点万物"小点会盖在主热区上面
        // （r14 回归抓到：点里侧椅子被"河鲜区"小点整个吃掉了）
        hotspotsRoot.MoveChild(layer, 0);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    /// <summary>发呆 12 秒 → 还没动过的地方轻轻呼吸</summary>
    public override void _Process(double delta)
    {
        if (chatOpen)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsBusy)
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
        if (phase == Phase.Dorm && !phoneSeenOnce && phoneGlow != null)
        {
            HotspotGlow.Pulse(phoneGlow, delay);
            delay += 0.4f;
        }
        else if (phase == Phase.Rmt && !cueDone && tableStarted == false && tableGlow != null)
        {
            HotspotGlow.Pulse(tableGlow, delay);
            delay += 0.4f;
        }
        else if (phase == Phase.Wjy && !seated)
        {
            if (seatNearGlow != null)
            {
                HotspotGlow.Pulse(seatNearGlow, delay);
                delay += 0.4f;
            }
            if (seatFarGlow != null)
                HotspotGlow.Pulse(seatFarGlow, delay);
        }
    }

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        flavorDorm.Visible = true;
        flavorRmt.Visible = false;
        flavorWjy.Visible = false;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();
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
            case Phase.Dorm:
                if (phoneSeenOnce)
                {
                    hintLabel.Visible = false;
                    return;
                }
                hintLabel.Text = DataStore.Text("hint.ch07_phone");
                return;

            case Phase.Rmt:
                if (cueDone || tableStarted)
                {
                    hintLabel.Visible = false;
                    return;
                }
                hintLabel.Text = DataStore.Text("hint.ch07_cue");
                return;

            case Phase.Wjy:
                hintLabel.Text = DataStore.Text(seated ? "hint.ch07_done" : "hint.ch07_seat");
                return;
        }
    }

    // ==================== 手机 ====================

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(phoneBtn);

        if (!phoneSeenOnce)
        {
            phoneSeenOnce = true;
            UpdateHint();
            dm.PlaySequence(ChapterId, "open_phone", OpenGroupChat);
            return;
        }

        // 宿舍阶段再点还是进群；到了球房/饭店之后，打开的是微信主页
        // （叶冰玉的请帖那些"活"的消息，都在主页的会话列表里）
        if (phase == Phase.Dorm)
            OpenGroupChat();
        else
            OpenWechatMain();
    }

    /// <summary>打开「今天的局」（内容在 data/chat/ch07_plan.json）。第一次收起手机会出门。</summary>
    private void OpenGroupChat()
    {
        chatOpen = true;
        bool firstTime = !chatSeenBefore;
        chatSeenBefore = true;

        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
            .Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () =>
        {
            chatOpen = false;
            lastActivityMsec = Time.GetTicksMsec();
            if (firstTime && !rmtStarted)
            {
                rmtStarted = true;
                UpdateHint();
                DialogueManager.Instance.PlaySequence(ChapterId, "after_phone", StartRmt);
            }
        };
        overlay.Open(GroupChatFile);
    }

    /// <summary>打开微信主页（会话列表：能看见新来的消息）</summary>
    private void OpenWechatMain()
    {
        chatOpen = true;
        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
            .Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () =>
        {
            chatOpen = false;
            lastActivityMsec = Time.GetTicksMsec();
        };
        overlay.Open(""); // 空字符串 = 微信主页
    }

    // ==================== 宿舍 → 罗曼蒂克 ====================

    /// <summary>出门：宿舍淡出，球房淡入，麻将声起</summary>
    private void StartRmt()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgRmt, 1.1f);
        FadeMahjong(-22f, 2.2f);
        DialogueManager.Instance.PlaySequence(ChapterId, "arrive", EnterRmt);
    }

    private void EnterRmt()
    {
        phase = Phase.Rmt;
        hotspotsRoot.Visible = true;
        flavorDorm.Visible = false;
        flavorRmt.Visible = true;
        flavorWjy.Visible = false;
        tableBtn.Visible = true;
        seatNearBtn.Visible = false;
        seatFarBtn.Visible = false;
        hintLabel.Visible = true;
        // 进了球房，放出台球演出层：自绘的白球正好"焊"在背景那颗静止白球上
        shot.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();
    }

    // ==================== 台球桌 ====================

    private void OnTablePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished || phase != Phase.Rmt || tableStarted)
            return;

        tableStarted = true;
        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(tableBtn);
        UpdateHint();
        dm.PlaySequence(ChapterId, "table_intro", OnTableIntroDone);
    }

    /// <summary>台词说完（心声微选择也在里面选完了）——轮到真打一杆</summary>
    private void OnTableIntroDone()
    {
        if (!GodotObject.IsInstanceValid(this))
            return;
        shot.Strike(() =>
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            DialogueManager.Instance.PlaySequence(ChapterId, "shot", OnMyShotDone);
        });
    }

    private void OnMyShotDone()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "shot2a", () =>
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            shot.Pocket(() =>
            {
                if (!GodotObject.IsInstanceValid(this))
                    return;
                DialogueManager.Instance.PlaySequence(ChapterId, "shot2b", OnHerShotDone);
            });
        });
    }

    private void OnHerShotDone()
    {
        cueDone = true;
        UpdateHint();
        DialogueManager.Instance.PlaySequence(ChapterId, "leave", StartWjy);
    }

    // ==================== 罗曼蒂克 → 皖江宴 ====================

    /// <summary>转场：球房淡出，包厢淡入，麻将声淡出</summary>
    private void StartWjy()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgWjy, 1.2f);
        FadeMahjong(-60f, 1.6f); // 出了球房，麻将声就留在门里了
        // 离开球房：收回台球演出层（不然自绘的白球会漂在包厢画面上）
        shot.Visible = false;
        DialogueManager.Instance.PlaySequence(ChapterId, "arrive_wjy", EnterWjy);
    }

    private void EnterWjy()
    {
        phase = Phase.Wjy;
        tableBtn.Visible = false;
        hotspotsRoot.Visible = true;
        flavorRmt.Visible = false;
        flavorWjy.Visible = true;
        seatNearBtn.Visible = true;
        seatFarBtn.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：饭吃到一半，叶冰玉的婚礼请帖悄悄到
        LiveEvents.Fire(this, ChapterId, "ch07_yebingyu_wedding");
    }

    // ==================== 选座位 ====================

    /// <summary>两张空椅子不是选项框，是直接点的（0 = 里侧，1 = 靠门）</summary>
    private void OnSeatPressed(int index)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished || phase != Phase.Wjy || seated)
            return;

        seated = true;
        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(index == 0 ? seatNearBtn : seatFarBtn);
        seatNearBtn.Visible = false;
        seatFarBtn.Visible = false;

        // 记一笔"坐哪儿"（结束页的选择回顾用；不加好感/勇气）
        RecordChoice("ch07_seat", index);
        UpdateHint();

        dm.PlaySequence(ChapterId, index == 0 ? "seat_near" : "seat_far", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "dinner", () =>
            {
                DialogueManager.Instance.PlaySequence(ChapterId, "topic_intro", OnTopicIntroDone);
            });
        });
    }

    private void OnTopicIntroDone()
    {
        DialogueManager.Instance.ShowChoice(ChapterId, "topic", OnTopicChosen);
    }

    private void OnTopicChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"topic_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "topic_end", PlayOutro);
        });
    }

    // ==================== 尾声 ====================

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

    /// <summary>把一张背景图淡进来（盖在之前的背景上面，alpha 从 0 到 1）</summary>
    private void CrossfadeTo(TextureRect layer, float seconds)
    {
        var tw = CreateTween();
        tw.TweenProperty(layer, "modulate:a", 1.0f, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>麻将声淡入 / 淡出（toDb 高于静音阈值就先开始播）</summary>
    private void FadeMahjong(float toDb, float seconds)
    {
        if (mahjongPlayer == null)
            return;
        if (toDb > -59f && !mahjongPlayer.Playing)
            mahjongPlayer.Play();
        var tw = CreateTween();
        tw.TweenProperty(mahjongPlayer, "volume_db", toDb, seconds);
    }
}
