using Godot;
using System.Collections.Generic;

/// <summary>
/// 第十六章：今天天气晴
///
/// 流程（2026 年 7 月 19 日周日，台风过境）：
///   蒙太奇   —— 3~7 月六个日子快闪（网咖天亮 / 她路过往楼下 / 答辩 / 五月晴天约定 / 毕业还耳机 / 车票）
///   实验室   —— 台风天，她三条消息（18:42 台风 / 18:43 我妈问我什么时候回来 / 18:51 没事）
///   备忘录   —— 他写下「今天天气晴」（five 个字，回收五月的约定）
///   最终选择 —— final 三选一：发给她（好感+5/勇气+10）/ 改成别的（+1/+2）/ 删掉（勇气−3）
///   尾声     —— 台风过境天真的晴了 → 接第十七章结局
///
/// 【这一章的重心】
/// 全流程最后一次大额加减。前面十五章攒下来的好感与勇气，在这里定生死——
/// 发出去的人，可能刚好跨过 85 的那条线；删掉的人，可能刚好掉到 60 以下。
/// 但这一章不告诉玩家阈值，只告诉玩家：那句话写出来了，光标在闪。
///
/// 【美术】TyphoonLabArt 程序绘制（台风天实验室 + 窗 + 雨 + 树），尾声推 Clear=1 放晴。
/// </summary>
public partial class Ch16 : ChapterBase
{
    private const string ChapterId = "ch16";

    // ==================== 章节参数（data/chapters/ch16.json） ====================

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

    private TyphoonLabArt art;
    private MontageReel montage;
    private Label hintLabel;

    private Control hotspotsRoot;
    private Control flavorLab;
    private Button phoneBtn;
    private Button windowBtn;
    private Control phoneGlow;
    private Control windowGlow;
    private Control phoneDot;   // 手机未读红点（她三条消息还热着）

    // ==================== 进度 ====================

    private enum Phase { Montage, Lab, Note, Final, Done }
    private Phase phase = Phase.Montage;

    private bool finished;

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    // ==================== 开场 ====================

    protected override void OnChapterReady()
    {
        GD.Print("第十六章：今天天气晴 —— 台风会过。话要说出去，才算数。");

        cfg = DataStore.LoadJson<ChapterConfig>("res://data/chapters/ch16.json") ?? new ChapterConfig();

        art = GetNode<TyphoonLabArt>("Lab");
        montage = GetNode<MontageReel>("Montage");
        hintLabel = GetNode<Label>("HintLabel");

        hotspotsRoot = GetNode<Control>("Hotspots");
        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        windowBtn = GetNode<Button>("Hotspots/WindowBtn");

        phoneBtn.Pressed += OnPhonePressed;
        windowBtn.Pressed += OnWindowPressed;
        phoneGlow = HotspotGlow.Attach(phoneBtn);
        windowGlow = HotspotGlow.Attach(windowBtn);

        // 手机右上角的未读红点（她三条消息还热着；开过手机就灭）
        phoneDot = new ChapterHudDot { Name = "PhoneNewDot" };
        phoneBtn.AddChild(phoneDot);

        flavorLab = MakeFlavorLayer("ch16_lab");

        UiSounds.WireAll(this);

        // 开场：台风正紧，屋里偏暗；蒙太奇先放
        StoryClock.ClearSegment();
        art.Storm = 1f;
        art.Clear = 0f;
        art.Dim = 0.45f;

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorLab.Visible = false;
        HideAllHotspots();

        var tw = CreateTween();
        tw.SetParallel(true);
        if (titleLabel != null)
            tw.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        if (subtitleLabel != null)
            tw.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        montage.Finished += StartLab;
        montage.Begin();
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
        if (phoneBtn != null) phoneBtn.Visible = false;
        if (windowBtn != null) windowBtn.Visible = false;
        if (phoneDot != null) phoneDot.Visible = false;
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
        if (phase == Phase.Lab)
        {
            HotspotGlow.Pulse(phoneDot != null && phoneDot.Visible ? phoneGlow : windowGlow, delay);
        }
    }

    // ==================== 蒙太奇 → 实验室 ====================

    private void StartLab()
    {
        phase = Phase.Lab;
        StoryClock.UseSegment("lab");

        // 她的三条台风消息这时候才在微信里可见（玩家开手机能看到）
        UnlockGate("storm");

        // 蒙太奇整体淡出（它自己铺着底色，淡掉之后实验室才露出来）
        if (montage != null && GodotObject.IsInstanceValid(montage))
        {
            var fade = CreateTween();
            fade.TweenProperty(montage, "modulate:a", 0.0f, 0.8f);
            fade.TweenCallback(Callable.From(() =>
            {
                if (montage != null && GodotObject.IsInstanceValid(montage))
                {
                    montage.Visible = false;
                    montage.QueueFree();
                }
            }));
        }

        var tw = CreateTween();
        tw.TweenProperty(art, "Dim", 0.22f, 1.6f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        DialogueManager.Instance.PlaySequence(ChapterId, "lab", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "heart_storm", ShowLabIdle);
        });
    }

    /// <summary>实验室可自由看：点手机（微信三条）/ 点窗外（接着写备忘录）</summary>
    private void ShowLabIdle()
    {
        phase = Phase.Lab;
        hotspotsRoot.Visible = true;
        flavorLab.Visible = true;
        phoneBtn.Visible = true;
        windowBtn.Visible = true;
        phoneDot.Visible = true;
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
            case Phase.Lab:
                ShowHint("hint.ch16_lab");
                break;
            case Phase.Note:
                ShowHint("hint.ch16_note");
                break;
            default:
                hintLabel.Visible = false;
                break;
        }
    }

    private void ShowHint(string key)
    {
        hintLabel.Visible = true;
        hintLabel.Text = DataStore.Text(key);
    }

    // ==================== 点手机：看她的三条消息 ====================

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Lab)
            return;

        HotspotGlow.Sparkle(phoneBtn);
        OpenPhone(cfg.JieChat, () =>
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            phoneDot.Visible = false;
            lastActivityMsec = Time.GetTicksMsec();
        });
    }

    private void OpenPhone(string chatId, System.Action onClose)
    {
        if (string.IsNullOrEmpty(chatId))
            return;
        var overlay = GD.Load<PackedScene>("res://scenes/ui/chat/chat_overlay.tscn")
            .Instantiate<ChatOverlay>();
        AddChild(overlay);
        overlay.Closed += () => onClose?.Invoke();
        overlay.Open(chatId);
    }

    // ==================== 点窗外：写备忘录 ====================

    private void OnWindowPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed || finished || phase != Phase.Lab)
            return;

        HotspotGlow.Sparkle(windowBtn);
        phase = Phase.Note;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        flavorLab.Visible = false;

        StoryClock.UseSegment("note");
        var tw = CreateTween();
        tw.TweenProperty(art, "Dim", 0.34f, 1.2f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        dm.PlaySequence(ChapterId, "note", ShowFinal);
    }

    // ==================== 最终选择 ====================

    private void ShowFinal()
    {
        StoryClock.UseSegment("final");
        DialogueManager.Instance.ShowChoice(ChapterId, "final", OnFinalChosen);
    }

    private void OnFinalChosen(int index)
    {
        // 三个分支各埋各的钥匙：微信里的回声 + 备忘录 App 的三态都靠它
        UnlockGate(index switch
        {
            0 => "sent",
            1 => "soft",
            _ => "deleted",
        });

        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, $"beat_{index}", StartOutro);
    }

    // ==================== 尾声：台风过境，天晴 ====================

    private void StartOutro()
    {
        StoryClock.UseSegment("dawn");

        // 音乐跟着天晴走：苦涩转释然（PianoBetween）→ 主旋律回归（Gym1，序章那首歌）
        // 整章的情绪出口在这里——画面放晴、雨停，那首歌回来了。
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -19f, 4.0f);

        // 雨停、天放晴、屋里亮起来（2.8 秒，慢，但看得见）
        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(art, "Storm", 0f, 2.2f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(art, "Clear", 1f, 2.8f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(art, "Dim", 0.04f, 2.4f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        DialogueManager.Instance.PlaySequence(ChapterId, "outro", FinishChapter);
    }

    /// <summary>
    /// 自检专用：直接从最终选择开始跑（跳过蒙太奇与实验室），验证 beat_X → outro → 结局页跳转。
    /// 驱动脚本 scene.call("DebugRunFinal", branch) 调用。
    /// </summary>
    public void DebugRunFinal(int branch)
    {
        if (finished)
            return;
        GD.Print($"[自检] 直接起最终选择分支 {branch}");
        OnFinalChosen(branch);
    }

    private void FinishChapter()
    {
        if (finished)
            return;
        finished = true;
        phase = Phase.Done;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        GameManager.Instance.RecordRelationshipEvent("jie", "ch16_final");

        // 结局章（第十七章）尚未实装——先接现有结束页
        GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
    }
}
