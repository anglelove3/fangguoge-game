using Godot;

/// <summary>
/// 第六章：放过自己
///
/// 流程：深夜烧烤摊（金艮请你吃串）→ 自己动手倒酒，一杯一层往事（4 杯）
///      → 第四杯之后的三选一（怎么接住"你从来没放过自己"这句话）
///      → 金艮的回话 → 碰杯前的心声微选择 → 两只杯子碰在一起
///      → 走回宿舍路上的尾声（那首歌又能听了）→ 试玩结束页
///
/// 【本章演出核心】
///   - 酒桌上的戏是真的：点热点倒酒，瓶身倾斜、酒线落进杯里、
///     两只杯子一起涨满，最后碰杯"叮"的一声（DrinkPour.cs 纯代码绘制）。
///   - 烤炉上飘着几点火星子（EmberDust）。
///   - 手机在桌边：妈发来一句"天凉了，记得加衣服"——打开手机能看见她。
/// </summary>
public partial class Ch06 : ChapterBase
{
    private const string ChapterId = "ch06";

    private Control hotspotsRoot;
    private Label hintLabel;
    private Button pourBtn;
    private Button phoneBtn;
    private Control pourGlow;
    private DrinkPour drink;

    private int pourCount;      // 已经倒了几杯（0~4）
    private bool pourBusy;      // 倒酒动画播放中
    private bool finished;      // 尾声播放中
    private bool chatOpen;      // 手机界面是否开着

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第六章：放过自己 —— 酒过三巡，才敢说的话");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -14f, 1.5f);

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        pourBtn = GetNode<Button>("Hotspots/PourBtn");
        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        drink = GetNode<DrinkPour>("Drink");

        pourBtn.Pressed += OnPourPressed;
        phoneBtn.Pressed += OnPhonePressed;
        pourGlow = HotspotGlow.Attach(pourBtn);
        HotspotGlow.Attach(phoneBtn);

        // "点万物有回应"：灯串、烤炉的白烟、一把空着的红塑料凳
        AddChild(FlavorSpots.Create(hotspotsRoot, ChapterId));

        // "手机活起来"：手机热点右上角挂未读小红点
        LiveEvents.AttachPhoneDot(phoneBtn);

        // 烤炉上空的火星子（挂在烤炉中上方的位置）
        var embers = new EmberDust { Position = new Vector2(775f, 690f) };
        AddChild(embers);

        UiSounds.WireAll(this);

        // 开场：先只有对话，热点等 intro 播完再放出来
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 酒瓶子轻轻呼吸一下（提醒还能倒酒）</summary>
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

        if (pourCount < 4 && pourGlow != null)
            HotspotGlow.Pulse(pourGlow, 0f);
    }

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：酒过三巡之前，妈发来一句"天凉了"
        LiveEvents.Fire(this, ChapterId, "ch06_ma_cold");
    }

    private void UpdateHint()
    {
        if (finished)
        {
            hintLabel.Visible = false;
            return;
        }

        if (pourCount < 4)
        {
            hintLabel.Text = DataStore.Text("hint.ch06", pourCount, 4);
            return;
        }

        hintLabel.Text = DataStore.Text("hint.ch06_done");
    }

    // ==================== 倒酒 ====================

    private void OnPourPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished || pourBusy || pourCount >= 4)
            return;

        pourBusy = true;
        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(pourBtn);

        pourCount++;
        UpdateHint();

        int cup = pourCount;
        drink.PourNext(() =>
        {
            pourBusy = false;
            if (!GodotObject.IsInstanceValid(this))
                return;
            lastActivityMsec = Time.GetTicksMsec();
            DialogueManager.Instance.PlaySequence(ChapterId, $"cup_{cup}", () =>
            {
                if (cup >= 4)
                    OnPoursDone();
            });
        });
    }

    /// <summary>四杯酒下肚——轮到那句话了</summary>
    private void OnPoursDone()
    {
        DialogueManager.Instance.ShowChoice(ChapterId, "reply", OnReplyChosen);
    }

    private void OnReplyChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"reply_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "toast", OnToastWordsDone);
        });
    }

    /// <summary>碰杯前的话都说完了（心声微选择也选完了）——轮到杯子</summary>
    private void OnToastWordsDone()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        drink.Cheers(() =>
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 2.5f);
                GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
            });
        });
    }

    // ==================== 手机 ====================

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(phoneBtn);

        chatOpen = true;
        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn").Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () =>
        {
            chatOpen = false;
            lastActivityMsec = Time.GetTicksMsec();
        };
        overlay.Open(""); // 空字符串 = 打开微信主页（会话列表，好让妈那条消息的红点被看见）
    }
}
