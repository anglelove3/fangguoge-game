using Godot;

/// <summary>
/// 第五章：晚安（第二十二轮新增——分手当夜）
///
/// 流程：咖啡馆出来（暮色）→ 摸黑回宿舍（室友不在，泡面碗还在）
///      → 掏手机，对着那个聊天框三选一（发「到家了吗」/ 打了又删 / 扣下洗脸）
///      → 妈的消息 → 关灯躺下，谁也没说晚安 → 凌晨，接第六章《手滑》
///
/// 【这一章是"空"的一章】没有数值起伏、没有新角色、甚至没有几句话——
/// 分手当夜本来就是这样：世界照常运转，只有你停下来了。
/// 三选一的正确答案不存在：发了没回、删了也好、洗脸也好，夜都得自己熬过去。
///
/// 【衔接】intro 紧接第三章咖啡馆分手（同一天深夜）；outro 的「睡不着」
/// 直接喂给第六章《手滑》的「凌晨一点半」。
/// </summary>
public partial class Ch05 : ChapterBase
{
    private const string ChapterId = "ch05";

    private Control hotspotsRoot;
    private Label hintLabel;
    private Button phoneBtn;
    private Button bedBtn;
    private Control phoneGlow;

    private bool phoneDone;  // 三选一做完了（妈的消息也跟着演完）
    private bool finished;   // 尾声播放中

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第五章：晚安 —— 分手当夜，谁也没说晚安");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmSadTrio, -15f, 2.0f); // 分手当夜——沉重比孤独更贴（第二十四轮 F35）

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        bedBtn = GetNode<Button>("Hotspots/BedBtn");

        phoneBtn.Pressed += OnPhonePressed;
        bedBtn.Pressed += OnBedPressed;

        phoneGlow = HotspotGlow.Attach(phoneBtn);
        HotspotGlow.Attach(bedBtn);

        // 热点按美术图坐标登记（背景 cover 缩放也不漂）
        // 第三轮反馈 11：换成床视角——手机就躺在被子上
        ArtAnchor.TrackFraction(phoneBtn, new Rect2(0.46f, 0.52f, 0.14f, 0.12f));
        ArtAnchor.TrackFraction(bedBtn, new Rect2(0.12f, 0.38f, 0.42f, 0.48f)); // 下铺（真的床）

        // "点万物有回应"（沿用宿舍那套：泡面碗、走廊声控灯）
        AddChild(FlavorSpots.Create(hotspotsRoot, "ch05"));

        UiSounds.WireAll(this);

        // 开场先只有对白
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        bedBtn.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "arrive", OnArriveFinished);
        });
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (!hotspotsRoot.Visible || finished)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();

        // 发呆：没做选择时手机呼吸，做完了就提醒睡下
        if (!phoneDone && phoneGlow != null)
            HotspotGlow.Pulse(phoneGlow, 0f);
    }

    private void OnArriveFinished()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "phone_lead", () =>
        {
            hotspotsRoot.Visible = true;
            phoneBtn.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();
            UpdateHint();
        });
    }

    private void UpdateHint()
    {
        if (finished)
        {
            hintLabel.Visible = false;
            return;
        }
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(phoneDone ? "hint.ch05_sleep" : "hint.ch05");
    }

    // ==================== 手机：三选一 ====================

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || finished || phoneDone)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(phoneBtn);
        hintLabel.Visible = false;

        DialogueManager.Instance.ShowChoice(ChapterId, "goodnight", OnGoodnightChosen);
    }

    private void OnGoodnightChosen(int index)
    {
        var dm = DialogueManager.Instance;
        // 微勇气：发了 2（敢问出口）/ 删了 0（话咽回去了）/ 洗脸 1（至少起身做了点什么）
        int courage = index == 0 ? 2 : index == 1 ? 0 : 1;
        GameManager.Instance.AddCourage(courage);

        // 第三轮 A1：选「删了」→ 先播逐字删除演出（那句话在屏幕上自己消失），再进对白
        System.Action playLines = () =>
        {
            dm.PlaySequence(ChapterId, $"choice_{index}", () =>
            {
                // 妈的消息：不管你选了哪条路，妈都会在这个点冒出来
                dm.PlaySequence(ChapterId, "mom_msg", () =>
                {
                    // 第三轮：出轨线的「发现时刻」——妈的消息之后，他睡不着，翻到她的 QQ 空间
                    dm.PlaySequence(ChapterId, "qq_hint", OpenQqDiscovery);
                });
            });
        };

        if (index == 1)
        {
            AddChild(new DraftErase("到家了吗", playLines));
        }
        else
        {
            playLines();
        }
    }

    /// <summary>
    /// 打开手机让他自己翻 QQ 空间（第三轮：伤害的发现由玩家亲手完成）——
    /// 点开 QQ → 空间 → 那张照片 → 发现演出 → 放下手机 → 台词「原来那天她说在加班」。
    /// </summary>
    private void OpenQqDiscovery()
    {
        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
            .Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () =>
        {
            lastActivityMsec = Time.GetTicksMsec();
            var dm = DialogueManager.Instance;
            string seq = QqSpaceView.DiscoveredOnce ? "qq_reveal" : "qq_skip";
            dm.PlaySequence(ChapterId, seq, () =>
            {
                phoneDone = true;
                bedBtn.Visible = true;
                UpdateHint();
            });
        };
        overlay.Open("");
    }

    // ==================== 睡下：接《手滑》 ====================

    private void OnBedPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || finished || !phoneDone)
            return;

        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        dm.PlaySequence(ChapterId, "outro", () =>
        {
            // 凌晨的宿舍——下一章《手滑》开场就是"凌晨一点半，手滑点开了旧聊天"
            GoToNextChapter("res://scenes/chapters/ch06/ch06.tscn", 6);
        });
    }
}
