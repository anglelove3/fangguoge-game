using Godot;
using System;

/// <summary>
/// 第十五章：那束光
///
/// 流程（一夜，四个时刻）：
///   街  21:45 —— 出校门往东，招牌在二楼，有一个笔画不亮
///   厅  22:05 —— 前台开卡（她抢着请客）、包间加二十
///   包间 22:20 —— 两台并排的机器 + 开黑三拍 + 桌上是能看的东西
///   深夜 02:10 —— 她靠过来睡着；核心选择 shoulder：盖外套 / 挡光 / 叫醒
///   凌晨 03:20 —— 她醒来，「你怎么不睡」
///
/// 【这一章的重心】
/// 不是"睡在一起"，是"把光挡住"。她睡着之前调暗了自己的屏幕、把椅子挪了半米——
/// 这事小到不值得说，但它和前面二十几章那种"算来算去"正好相反：
/// 这一次他什么都没算，只是把光挡住了。
///
/// 【美术】ImageGen 从 10-18 起不通，场景由 NetcafeArt 程序绘制；
/// 深夜把 Dim 推到 0.85（灯管只留一根），她那台屏幕的亮度 HerScreen 会在"挡光"分支里压下去。
/// </summary>
public partial class Ch15 : ChapterBase
{
    private const string ChapterId = "ch15";

    // ==================== 节点 ====================

    private NetcafeArt art;
    private ColorRect veil;
    private Label neonLabel;
    private Label hintLabel;

    private Control hotspotsRoot;
    private Control flavorHall;
    private Control flavorBooth;

    private Button qteBtn;
    private Button waitBtn;
    private Control qteGlow;
    private Control waitGlow;

    private LanPartyQte qte;

    // ==================== 进度 ====================

    private enum Phase { Street, Hall, Booth, Deep, Done }
    private Phase phase = Phase.Street;

    private bool qteDone;
    private bool finished;

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十五章：那束光 —— 不用算里程的那一夜");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -18f, 2.0f);

        art = GetNode<NetcafeArt>("Netcafe");
        veil = GetNode<ColorRect>("Veil");
        neonLabel = GetNode<Label>("NeonLabel");
        hintLabel = GetNode<Label>("HintLabel");

        hotspotsRoot = GetNode<Control>("Hotspots");
        qteBtn = GetNode<Button>("Hotspots/QteBtn");
        waitBtn = GetNode<Button>("Hotspots/WaitBtn");
        qte = GetNode<LanPartyQte>("Qte");

        qteBtn.Pressed += OnQtePressed;
        waitBtn.Pressed += OnWaitPressed;

        qteGlow = HotspotGlow.Attach(qteBtn);
        waitGlow = HotspotGlow.Attach(waitBtn);

        ArtAnchor.TrackFraction(qteBtn, new Rect2(0.455f, 0.700f, 0.090f, 0.100f));
        ArtAnchor.TrackFraction(waitBtn, new Rect2(0.455f, 0.700f, 0.090f, 0.100f));

        flavorHall = MakeFlavorLayer("ch15_hall");
        flavorBooth = MakeFlavorLayer("ch15_booth");

        UiSounds.WireAll(this);

        // 街那一段：网咖还看不见，先压成一块深色底（文字落在上面）
        StoryClock.ClearSegment();
        art.Mode = NetcafeArt.ViewMode.Hall;
        art.Dim = 1f;
        veil.Modulate = new Color(1, 1, 1, 1);
        neonLabel.Visible = false;

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        qte.Visible = false;
        HideAllHotspots();
        flavorHall.Visible = false;
        flavorBooth.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "street", StartHall);
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
        if (qteBtn != null) qteBtn.Visible = false;
        if (waitBtn != null) waitBtn.Visible = false;
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
        if (phase == Phase.Booth && !qteDone)
        {
            HotspotGlow.Pulse(qteGlow, delay);
        }
        else if (phase == Phase.Booth && qteDone)
        {
            HotspotGlow.Pulse(waitGlow, delay);
        }
    }

    // ==================== 提示语 ====================

    private void UpdateHint()
    {
        if (finished)
        {
            hintLabel.Visible = false;
            return;
        }

        if (phase == Phase.Booth)
        {
            ShowHint(qteDone ? "hint.ch15_after_qte" : "hint.ch15_booth");
            return;
        }

        hintLabel.Visible = false;
    }

    private void ShowHint(string key)
    {
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(key);
    }

    // ==================== 第 1 段：街 → 大厅 ====================

    private void StartHall()
    {
        phase = Phase.Hall;
        StoryClock.UseSegment("hall");

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(veil, "modulate:a", 0.0f, 1.4f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(art, "Dim", 0.25f, 1.6f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.Chain().TweenCallback(Callable.From(() =>
        {
            neonLabel.Visible = true;
            var fade = CreateTween();
            fade.TweenProperty(neonLabel, "modulate:a", 1.0f, 0.8f);
        }));

        DialogueManager.Instance.PlaySequence(ChapterId, "hall", StartBooth);
    }

    // ==================== 第 2 段：包间 ====================

    private void StartBooth()
    {
        phase = Phase.Booth;
        StoryClock.UseSegment("booth");
        neonLabel.Visible = false;
        flavorHall.Visible = false;

        art.Mode = NetcafeArt.ViewMode.Booth;
        var tw = CreateTween();
        tw.TweenProperty(art, "Dim", 0.0f, 1.2f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        DialogueManager.Instance.PlaySequence(ChapterId, "booth", () =>
        {
            hotspotsRoot.Visible = true;
            flavorBooth.Visible = true;
            qteBtn.Visible = true;
            waitBtn.Visible = false;
            lastActivityMsec = Time.GetTicksMsec();
            UpdateHint();
        });
    }

    // ==================== 开黑三拍 ====================

    private void OnQtePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Booth || qteDone)
            return;

        HotspotGlow.Sparkle(qteBtn);
        qteBtn.Visible = false;
        flavorBooth.Visible = false;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        lastActivityMsec = Time.GetTicksMsec();

        dm.PlaySequence(ChapterId, "qte_intro", () =>
        {
            ShowHint("hint.ch15_qte");
            qte.Begin(OnQteFinished);
        });
    }

    private void OnQteFinished(int hits)
    {
        hintLabel.Visible = false;
        qteDone = true;

        // 三拍里命中两拍以上算赢；只影响台词与一点勇气，不卡流程
        bool win = hits >= 2;
        RecordChoice("ch15_qte", win ? 0 : 1);
        GameManager.Instance.AddCourage(win ? 2 : 1);

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, win ? "qte_win" : "qte_lose", () =>
        {
            hotspotsRoot.Visible = true;
            flavorBooth.Visible = true;
            waitBtn.Visible = true;
            lastActivityMsec = Time.GetTicksMsec();
            phase = Phase.Booth;
            UpdateHint();
        });
    }

    // ==================== 第 3 段：深夜 ====================

    private void OnWaitPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Booth || !qteDone)
            return;

        HotspotGlow.Sparkle(waitBtn);
        phase = Phase.Deep;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorBooth.Visible = false;

        StoryClock.UseSegment("deep");
        var tw = CreateTween();
        tw.TweenProperty(art, "Dim", 0.85f, 1.8f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        dm.PlaySequence(ChapterId, "deep", () =>
        {
            dm.PlaySequence(ChapterId, "deep_heart", () =>
            {
                dm.ShowChoice(ChapterId, "shoulder", OnShoulderChosen);
            });
        });
    }

    private void OnShoulderChosen(int index)
    {
        // 1 = 挡光：先把她的屏幕压暗，再演
        if (index == 1)
        {
            var tw = CreateTween();
            tw.TweenProperty(art, "HerScreen", 0.10f, 1.1f)
              .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, $"beat_{index}", () =>
        {
            StoryClock.UseSegment("dawn");
            var tw = CreateTween();
            tw.TweenProperty(art, "Dim", 0.45f, 2.5f)
              .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

            dm.PlaySequence(ChapterId, "dawn", () =>
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
        phase = Phase.Done;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        GameManager.Instance.RecordRelationshipEvent("jie", "ch15_shoulder");

        GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
    }
}
