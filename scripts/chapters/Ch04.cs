using Godot;
using System.Collections.Generic;

/// <summary>
/// 第四章：手滑
///
/// 流程：分手当夜凌晨的宿舍 → 打开旧聊天（只读 + 幽灵打字）
///      → 深夜三选一（出去走走 / 继续看手机 / 打电话给朋友）
///      → 天亮后实验室（换背景）→ 尾声 → DEMO 结束页
///
/// 【本章彩蛋】烟灰缸（可反复点，烟头数量递进）、旧耳机（隐藏物品）。
/// 【本章演出核心】旧聊天是"只读"的：输入框打不了字，
/// 却会自己一个字一个字打出「最近还好吗」，再一个字一个字删掉。
/// </summary>
public partial class Ch04 : ChapterBase
{
    private const string ChapterId = "ch04"; // 对应 data/dialogues/ch04.json

    private TextureRect background;
    private Control hotspotsRoot;
    private Label hintLabel;

    private bool phoneSeenOnce;   // 手机已看过一次（主线 1/1）
    private bool chatSeenBefore;  // 旧聊天打开过（之后的感想对白只播一次）
    private bool nightStarted;    // 深夜选择已经触发
    private bool finished;        // 尾声播放中
    private int ashtrayStage;     // 烟灰缸彩蛋递进
    private bool earphoneTaken;   // 旧耳机已收走
    private bool chatOpen;        // 聊天界面是否开着

    private readonly Dictionary<string, Control> glows = new();

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第四章：手滑 —— 凌晨一点半，手滑点开了旧聊天");

        // 第十一轮 B4：这一章是深夜，换第三首吉姆诺佩蒂（更慢、更空）
        // 第十一轮 B4：这一章是深夜，换第三首吉姆诺佩蒂（更慢、更空）
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -15f, 1.5f);

        background = GetNode<TextureRect>("Background");
        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        var phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        var ashtrayBtn = GetNode<Button>("Hotspots/AshtrayBtn");
        var earphoneBtn = GetNode<Button>("Hotspots/EarphoneBtn");
        phoneBtn.Pressed += OnPhonePressed;
        ashtrayBtn.Pressed += () => OnAshtrayPressed(ashtrayBtn);
        earphoneBtn.Pressed += () => OnEarphonePressed(earphoneBtn);

        glows["phone"] = HotspotGlow.Attach(phoneBtn);

        // 第十五轮：热点按"美术图坐标"登记（手机 = 美术实测位置），窗口不成 16:9 也不漂
        ArtAnchor.Track(phoneBtn, new Rect2(1616f, 880f, 168f, 88f));
        ArtAnchor.TrackFraction(ashtrayBtn, new Rect2(0.565f, 0.7f, 0.14f, 0.13f));
        ArtAnchor.TrackFraction(earphoneBtn, new Rect2(0.615f, 0.595f, 0.085f, 0.095f));

        // "点万物有回应"：窗外的灯、柜门上的照片、那两桶泡面（data/flavor/ch04.json）
        AddChild(FlavorSpots.Create(hotspotsRoot, "ch04"));

        // "手机活起来"：手机热点右上角挂小红点（有未读消息时自己亮起来）
        LiveEvents.AttachPhoneDot(phoneBtn);

        UiSounds.WireAll(this);

        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 手机热点轻轻呼吸（引导去看旧聊天）</summary>
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

        if (!hotspotsRoot.Visible || nightStarted)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();

        if (!phoneSeenOnce && glows.TryGetValue("phone", out var glow) && glow != null)
            HotspotGlow.Pulse(glow, 0f);
    }

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：凌晨的宿舍里，li-lab 群有人还在报仪器空档
        LiveEvents.Fire(this, "ch04", "ch04_sanjin_lab");

        // 分手之后，宝宝那头迟到的两句收尾（对不起 / 照顾好自己）
        LiveEvents.Fire(this, "ch04", "ch04_baobao_sorry");
        LiveEvents.Fire(this, "ch04", "ch04_baobao_care");
    }

    private void UpdateHint()
    {
        if (nightStarted)
        {
            hintLabel.Visible = false;
            return;
        }
        hintLabel.Text = phoneSeenOnce
            ? DataStore.Text("hint.ch04_done")
            : DataStore.Text("hint.ch04", phoneSeenOnce ? 1 : 0, 1);
    }

    // ==================== 手机：打开旧聊天 ====================

    private void OnPhonePressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        var btn = GetNode<Button>("Hotspots/PhoneBtn");
        HotspotGlow.Sparkle(btn);

        if (!phoneSeenOnce)
        {
            phoneSeenOnce = true;
            UpdateHint();
            dm.PlaySequence(ChapterId, "open_phone", OpenOldChat);
        }
        else
        {
            OpenOldChat();
        }
    }

    /// <summary>
    /// 打开和她的旧聊天（只读 + 幽灵打字，内容在 data/chat/ch04_old_chat.json）。
    /// 第一次收起手机后会播"打了又删"的感想，然后进入深夜选择。
    /// </summary>
    private void OpenOldChat()
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
            if (firstTime && !nightStarted)
            {
                nightStarted = true;
                UpdateHint();
                DialogueManager.Instance.PlaySequence(ChapterId, "after_chat", () =>
                {
                    DialogueManager.Instance.ShowChoice(ChapterId, "night", OnNightChosen);
                });
            }
        };
        overlay.Open("ch04_old_chat");
    }

    // ==================== 彩蛋热点 ====================

    private void OnAshtrayPressed(Button btn)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(btn);
        ashtrayStage++;
        string seq = ashtrayStage switch
        {
            1 => "egg_ashtray_1",
            2 => "egg_ashtray_2",
            3 => "egg_ashtray_3",
            _ => "egg_ashtray_more",
        };
        dm.PlaySequence(ChapterId, seq, null);
    }

    private void OnEarphonePressed(Button btn)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || earphoneTaken)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        earphoneTaken = true;
        HotspotGlow.Sparkle(btn);
        btn.Disabled = true;
        dm.PlaySequence(ChapterId, "explore_earphone", () => FindHiddenItem("earphone"));
    }

    // ==================== 深夜选择 → 天亮收尾 ====================

    private void OnNightChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"night_{index}", PlayOutro);
    }

    /// <summary>天亮了：背景从深夜宿舍淡切到白天的实验室，再接尾声</summary>
    private void PlayOutro()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        var dayLab = GD.Load<Texture2D>("res://assets/art/backgrounds/day_lab_v1.png");
        // 天亮了：深夜那首退场，回到主题曲（跟着画面一起淡过来）
        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 2.2f);
        var tw = CreateTween();
        tw.TweenProperty(background, "modulate:a", 0f, 0.8);
        tw.TweenCallback(Callable.From(() => background.Texture = dayLab));
        tw.TweenProperty(background, "modulate:a", 1f, 1.0);
        tw.TweenInterval(0.4);
        tw.TweenCallback(Callable.From(() =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                // r13：第四章之后不再是"试玩结束"，接上正式剧情 第五章《碧蓝蓝天》
                GoToNextChapter("res://scenes/chapters/ch05/ch05.tscn", 5);
            });
        }));
    }
}
