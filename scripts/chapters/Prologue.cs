using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 序章：那首歌
/// 继承 ChapterBase，自动拥有标题显示、返回菜单、自动存档等功能
///
/// 流程：标题卡 → 开场对白 → 探索三件物品（电脑/耳机/窗外）
///      → 齐了之后室友给外号起选项 → 选完的后续对白 → 进入第一章
///
/// 【第十五轮】电脑变成真的了：点开能开关机、翻数据、看论文、读邮件
/// （ComputerOverlay），而且随时可以再坐回去用。
///
/// 【知识点 - 回调链】
/// PlaySequence / ShowChoice 的最后一个参数是"结束后要做什么"，
/// 一层套一层形成流程链，就像多米诺骨牌一样一张推一张。
/// </summary>
public partial class Prologue : ChapterBase
{
    private const string ChapterId = "prologue"; // 对应 data/dialogues/prologue.json

    // 已经看过的物品（用 Set 防止重复触发）
    private readonly HashSet<string> foundItems = new();

    private Control hotspotsRoot;
    private Label hintLabel;
    private Control titleCard;

    // 每个热点的柔光层（itemId → 柔光 Control，发呆提示用）
    private readonly Dictionary<string, Control> glows = new();

    // 玩家上一次"有进展"的时间（毫秒），用来判断是不是发呆了
    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000; // 12 秒没动静就给个微光提示

    public override void _Ready()
    {
        base._Ready(); // 基类负责找节点、绑定返回按钮、存档
    }

    /// <summary>
    /// 重写基类的虚方法，写序章特有的逻辑
    /// </summary>
    protected override void OnChapterReady()
    {
        GD.Print("序章：那首歌 —— 深夜实验室，循环播放《开始懂了》");

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        titleCard = GetNode<Control>("TitleCard");

        // 绑定三个物品热点按钮（点击完全隐形，鼠标悬停才发光）
        var computerBtn = GetNode<Button>("Hotspots/ComputerBtn");
        var earphonesBtn = GetNode<Button>("Hotspots/EarphonesBtn");
        var windowBtn = GetNode<Button>("Hotspots/WindowBtn");
        computerBtn.Pressed += () => OnHotspotPressed("computer", "Hotspots/ComputerBtn");
        earphonesBtn.Pressed += () => OnHotspotPressed("earphones", "Hotspots/EarphonesBtn");
        windowBtn.Pressed += () => OnHotspotPressed("window", "Hotspots/WindowBtn");

        // 给每个热点挂上隐形微光层（发呆提示用）
        glows["computer"] = HotspotGlow.Attach(computerBtn);
        glows["earphones"] = HotspotGlow.Attach(earphonesBtn);
        glows["window"] = HotspotGlow.Attach(windowBtn);

        // 第十五轮：热点按"美术图坐标"登记（窗口不成 16:9 时背景会偏移，热点跟着走）
        ArtAnchor.TrackFraction(computerBtn, new Rect2(0.12f, 0.16f, 0.32f, 0.46f));
        ArtAnchor.TrackFraction(earphonesBtn, new Rect2(0.5f, 0.48f, 0.18f, 0.16f));
        ArtAnchor.TrackFraction(windowBtn, new Rect2(0.48f, 0.02f, 0.51f, 0.43f));

        // "点万物有回应"：墙角、桌面、桌肚……没任务的小地方（data/flavor/prologue.json）
        AddChild(FlavorSpots.Create(hotspotsRoot, "prologue"));

        // 标题卡：点任意位置开始
        var startOverlay = GetNode<Button>("TitleCard/StartOverlay");
        startOverlay.Pressed += BeginIntro;

        // 全场景按钮音效（热点悬停"嗒"、点击"咔"）
        UiSounds.WireAll(this);

        // 开场先藏起热点提示
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;
    }

    /// <summary>
    /// 每帧检查：玩家长时间没进展 & 没在看对话 → 让还没找到的热点轻轻呼吸一下
    /// </summary>
    public override void _Process(double delta)
    {
        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsBusy)
        {
            // 正在看对话不算发呆，计时器跟着走
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (!hotspotsRoot.Visible || foundItems.Count >= 3)
            return;

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();
        PulseUnfoundHotspots();
    }

    private void PulseUnfoundHotspots()
    {
        float delay = 0f;
        foreach (var (itemId, glow) in glows)
        {
            if (foundItems.Contains(itemId))
                continue;
            HotspotGlow.Pulse(glow, delay);
            delay += 0.4f; // 错开闪，像波浪扫过去
        }
    }

    /// <summary>
    /// 标题卡淡出 → 播放开场对白
    /// </summary>
    private void BeginIntro()
    {
        var startOverlay = GetNode<Button>("TitleCard/StartOverlay");
        startOverlay.Disabled = true; // 防止连点触发两次

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleCard, "modulate:a", 0.0f, 0.7f);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 0.7f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 0.7f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            titleCard.Visible = false;
            titleLabel.Visible = false;
            subtitleLabel.Visible = false;

            DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
        }));
    }

    /// <summary>
    /// 开场对白播完 → 显示热点，进入探索阶段
    /// </summary>
    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：探索的时候，手机自己震一下（她深夜发来一句）
        LiveEvents.Fire(this, "prologue", "prologue_baobao_night");
    }

    private void UpdateHint()
    {
        int n = foundItems.Count;
        hintLabel.Text = n >= 3
            ? DataStore.Text("hint.prologue_done")
            : DataStore.Text("hint.prologue", n, 3);
    }

    /// <summary>
    /// 点击物品热点：播放对应的探索对白，看完后按钮失效
    /// （看不到"已看过"的框——这条线索是隐形的，进度看顶部提示）
    ///
    /// 【第十五轮】电脑不再"看完即封"：对白说完屏幕就亮起来，玩家在里面
    /// 翻数据、看论文、读邮件、还能关机再开机；之后随时想再坐回去都行。
    /// </summary>
    private void OnHotspotPressed(string itemId, string buttonPath)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed)
            return; // 对话进行中 / 手机背包电脑开着一个 → 忽略

        bool alreadyFound = foundItems.Contains(itemId);
        if (alreadyFound && itemId != "computer")
            return; // 其他两样看过了就是看过了

        var btn = GetNode<Button>(buttonPath);
        lastActivityMsec = Time.GetTicksMsec();

        if (!alreadyFound)
        {
            foundItems.Add(itemId);

            // 点中的瞬间：指尖冒出两三颗小星光（"找到了！"的反馈，没有框）
            HotspotGlow.Sparkle(btn);
            if (itemId != "computer")
                btn.Disabled = true; // 耳机/窗户看过了封上；电脑不封，随时还能再开
            UpdateHint();
        }

        if (itemId == "computer")
        {
            if (alreadyFound)
            {
                OpenComputer(null); // 再坐回去，直接开机
            }
            else
            {
                // 先说完那两句感慨，屏幕亮起来；等玩家起身离开，再看要不要进下一段
                dm.PlaySequence(ChapterId, "explore_computer", () => OpenComputer(() =>
                {
                    if (foundItems.Count >= 3)
                        PlayAllFound();
                }));
            }
            return;
        }

        dm.PlaySequence(ChapterId, $"explore_{itemId}", () =>
        {
            // 三样都看完 → 室友回来给外号选项
            if (foundItems.Count >= 3)
            {
                PlayAllFound();
            }
        });
    }

    /// <summary>打开电脑界面（全屏模态；和背包/手机共享输入抑制）</summary>
    private void OpenComputer(Action onClosed)
    {
        if (DialogueManager.Instance?.IsUiSuppressed == true)
            return;

        var pc = new ComputerOverlay();
        pc.Name = "ComputerOverlay"; // 显式命名：自动化测试/调试面板靠它找
        AddChild(pc);
        if (onClosed != null)
            pc.Closed += onClosed;
        pc.Open();
    }

    private void PlayAllFound()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "all_found", () =>
        {
            DialogueManager.Instance.ShowChoice(ChapterId, "nickname", OnNicknameChosen);
        });
    }

    /// <summary>
    /// 外号选完（index = 0/1/2）→ 各自的后续对白 → 尾声 → 第一章
    /// </summary>
    private void OnNicknameChosen(int index)
    {
        DialogueManager.Instance.PlaySequence(ChapterId, $"nickname_{index}", () =>
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                GoToNextChapter("res://scenes/chapters/ch01/ch01.tscn", 1);
            });
        });
    }
}
