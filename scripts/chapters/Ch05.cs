using Godot;
using System.Collections.Generic;

/// <summary>
/// 第五章：碧蓝蓝天
///
/// 流程：傍晚的实验室（手机震个不停）→ 打开「今天吃什么研讨组」
///      → 收起手机后三选一（去 / 半推半就地去 / 让他们定）
///      → 骑车穿过黄昏的校园（换背景）→「兰亭雨水」的雨夜饭局
///      → 三个热点：看菜单（还记得第一章点过什么）/ 聊近况 / 听雨（心声微选择）
///      → 宿舍开黑的尾声 → 第六章
///
/// 【本章演出核心】
///   - 手机里的群聊是"活"的：三个人轮番叫你，最后一条是「来了就行」。
///   - 进店之后雨声开始下（程序生成的 8 秒无缝循环），散场时雨停。
///   - 点菜那句话记得你在第一章的选择（menu_echo_la）。
/// </summary>
public partial class Ch05 : ChapterBase
{
    private const string ChapterId = "ch05";
    private const string GroupChatFile = "ch05_group"; // 对应 data/chat/ch05_group.json

    private TextureRect bgDayLab;
    private TextureRect bgCampus;
    private TextureRect bgLanting;
    private TextureRect bgDorm;

    private Control hotspotsRoot;
    private Control flavorLab;
    private Control flavorLanting;
    private Label hintLabel;

    private Button phoneBtn;
    private Button menuBtn;
    private Button friendsBtn;
    private Button rainBtn;

    private AudioStreamPlayer rainPlayer;

    private bool phoneSeenOnce;   // 手机看过一次（主线 1/1）
    private bool chatSeenBefore;  // 群聊打开过（之后的感想对白只播一次）
    private bool comfortStarted;  // 傍晚选择已经触发
    private bool arrivedLanting;  // 已经进店（兰亭阶段）
    private bool finished;        // 尾声播放中
    private bool chatOpen;        // 手机界面是否开着

    private readonly HashSet<string> doneSpots = new(); // menu / friends / rain

    private readonly Dictionary<string, Control> glows = new();

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第五章：碧蓝蓝天 —— 被朋友捞出来的那个傍晚");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 1.5f);

        bgDayLab = GetNode<TextureRect>("Background");
        bgCampus = GetNode<TextureRect>("CampusDusk");
        bgLanting = GetNode<TextureRect>("LantingRain");
        bgDorm = GetNode<TextureRect>("DormNight");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");

        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        menuBtn = GetNode<Button>("Hotspots/MenuBtn");
        friendsBtn = GetNode<Button>("Hotspots/FriendsBtn");
        rainBtn = GetNode<Button>("Hotspots/RainBtn");

        phoneBtn.Pressed += OnPhonePressed;
        menuBtn.Pressed += () => OnSpotPressed("menu", menuBtn);
        friendsBtn.Pressed += () => OnSpotPressed("friends", friendsBtn);
        rainBtn.Pressed += () => OnSpotPressed("rain", rainBtn);

        glows["phone"] = HotspotGlow.Attach(phoneBtn);
        glows["menu"] = HotspotGlow.Attach(menuBtn);
        glows["friends"] = HotspotGlow.Attach(friendsBtn);
        glows["rain"] = HotspotGlow.Attach(rainBtn);

        // "点万物有回应"：实验室的和兰亭的各一套，跟着阶段开关
        flavorLab = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        flavorLab.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hotspotsRoot.AddChild(flavorLab);
        AddChild(FlavorSpots.Create(flavorLab, "ch05_lab"));

        flavorLanting = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        flavorLanting.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hotspotsRoot.AddChild(flavorLanting);
        AddChild(FlavorSpots.Create(flavorLanting, "ch05_lanting"));

        // "手机活起来"：手机热点右上角挂小红点
        LiveEvents.AttachPhoneDot(phoneBtn);

        // 雨声：程序生成的 8 秒无缝循环，进店时淡入、散场时淡出
        rainPlayer = new AudioStreamPlayer { VolumeDb = -60f };
        var rainStream = GD.Load<AudioStream>("res://assets/audio/sfx/sfx_rain.wav");
        if (rainStream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);
        }
        rainPlayer.Stream = rainStream;
        AddChild(rainPlayer);

        UiSounds.WireAll(this);

        // 开场：只有实验室阶段能看
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        menuBtn.Visible = false;
        friendsBtn.Visible = false;
        rainBtn.Visible = false;
        flavorLanting.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
    }

    /// <summary>发呆 12 秒 → 还没点过的地方轻轻呼吸</summary>
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
        if (!phoneSeenOnce && glows.TryGetValue("phone", out var pg) && pg != null)
        {
            HotspotGlow.Pulse(pg, delay);
            delay += 0.4f;
        }

        if (arrivedLanting)
        {
            foreach (var id in new[] { "menu", "friends", "rain" })
            {
                if (doneSpots.Contains(id))
                    continue;
                if (glows.TryGetValue(id, out var g) && g != null)
                {
                    HotspotGlow.Pulse(g, delay);
                    delay += 0.4f;
                }
            }
        }
    }

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        flavorLab.Visible = true;
        flavorLanting.Visible = false;
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

        if (!arrivedLanting)
        {
            // 实验室阶段：先看手机；看过之后、出发之前，提示先收起来
            if (phoneSeenOnce)
            {
                hintLabel.Visible = false;
                return;
            }
            hintLabel.Text = DataStore.Text("hint.ch05_phone");
            return;
        }

        if (doneSpots.Count >= 3)
        {
            hintLabel.Text = DataStore.Text("hint.ch05_done");
            return;
        }

        hintLabel.Text = DataStore.Text("hint.ch05", doneSpots.Count, 3);
    }

    // ==================== 手机：打开群聊 ====================

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
        }
        else
        {
            OpenGroupChat();
        }
    }

    /// <summary>
    /// 打开「今天吃什么研讨组」（内容在 data/chat/ch05_group.json）。
    /// 第一次收起手机后会播一段感想，然后进入傍晚的三选一。
    /// </summary>
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
            if (firstTime && !comfortStarted)
            {
                comfortStarted = true;
                UpdateHint();
                DialogueManager.Instance.PlaySequence(ChapterId, "after_chat", () =>
                {
                    DialogueManager.Instance.ShowChoice(ChapterId, "comfort", OnComfortChosen);
                });
            }
        };
        overlay.Open(GroupChatFile);
    }

    // ==================== 傍晚选择 → 骑车 → 进店 ====================

    private void OnComfortChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"comfort_{index}", StartRide);
    }

    /// <summary>出发：实验室淡出，校园黄昏淡入</summary>
    private void StartRide()
    {
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
        CrossfadeTo(bgCampus, 1.0f);
        DialogueManager.Instance.PlaySequence(ChapterId, "ride", ArriveLanting);
    }

    /// <summary>到湖边：黄昏淡出，兰亭雨夜淡入，雨声开始下</summary>
    private void ArriveLanting()
    {
        CrossfadeTo(bgLanting, 1.1f);
        FadeRain(-20f, 2.0f);
        DialogueManager.Instance.PlaySequence(ChapterId, "arrive", EnterLanting);
    }

    private void EnterLanting()
    {
        arrivedLanting = true;
        hotspotsRoot.Visible = true;
        flavorLab.Visible = false;
        flavorLanting.Visible = true;
        menuBtn.Visible = true;
        friendsBtn.Visible = true;
        rainBtn.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：饭吃到一半，爸发来"最近忙不忙"
        LiveEvents.Fire(this, ChapterId, "ch05_ba_ask");
    }

    // ==================== 兰亭三个热点 ====================

    private void OnSpotPressed(string id, Button btn)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished || !arrivedLanting)
            return;
        if (id != "friends" && doneSpots.Contains(id))
            return; // 菜单和雨只点一次；近况可以反复聊

        lastActivityMsec = Time.GetTicksMsec();
        HotspotGlow.Sparkle(btn);

        if (id == "friends")
        {
            bool firstTime = doneSpots.Add("friends");
            UpdateHint();
            dm.PlaySequence(ChapterId, firstTime ? "friends_1" : "friends_more", CheckAllFound);
            return;
        }

        doneSpots.Add(id);
        UpdateHint();

        if (id == "menu")
        {
            // 还记得第一章点过什么吗？（点了辣子鸡的人，多一段专属台词）
            bool laZiJi = GameManager.Instance.ChoiceHistory.TryGetValue("ch01_order_food", out int v) && v == 0;
            dm.PlaySequence(ChapterId, laZiJi ? "menu_echo_la" : "menu_echo", CheckAllFound);
            return;
        }

        // rain：一段引导 + 心声微选择（heart "rain"）
        dm.PlaySequence(ChapterId, "rain", CheckAllFound);
    }

    private void CheckAllFound()
    {
        if (doneSpots.Count >= 3)
            PlayOutro();
    }

    /// <summary>散场：最后一桌笑完 → 转到宿舍 → 尾声 → 第六章</summary>
    private void PlayOutro()
    {
        if (finished)
            return;
        finished = true;
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        DialogueManager.Instance.PlaySequence(ChapterId, "all_found", () =>
        {
            CrossfadeTo(bgDorm, 1.2f);
            FadeRain(-60f, 1.6f); // 散场的时候，雨停了
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GoToNextChapter("res://scenes/chapters/ch06/ch06.tscn", 6);
            });
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

    /// <summary>雨声淡入 / 淡出（toDb 高于静音阈值就先开始播）</summary>
    private void FadeRain(float toDb, float seconds)
    {
        if (rainPlayer == null)
            return;
        if (toDb > -59f && !rainPlayer.Playing)
            rainPlayer.Play();
        var tw = CreateTween();
        tw.TweenProperty(rainPlayer, "volume_db", toDb, seconds);
    }
}
