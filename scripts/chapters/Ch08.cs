using Godot;

/// <summary>
/// 第八章：放过自己
///
/// 流程：深夜烧烤摊（金艮请你吃串）→ 自己动手倒酒，一杯一层往事（4 杯）
///      → 第四杯之后的三选一（怎么接住"你从来没放过自己"这句话）
///      → 金艮的回话 → 碰杯前的心声微选择 → 两只杯子碰在一起
///      → 走回宿舍路上的尾声（那首歌又能听了）→ 第八章《三点水》
///
/// 【本章演出核心】
///   - 酒桌上的戏是真的：点热点倒酒，瓶身倾斜、酒线落进杯里、
///     两只杯子一起涨满，最后碰杯"叮"的一声（DrinkPour.cs 纯代码绘制）。
///   - 烤炉上飘着几点火星子（EmberDust）。
///   - 手机在桌边：妈发来一句"天凉了，记得加衣服"——打开手机能看见她。
/// </summary>
public partial class Ch08 : ChapterBase
{
    private const string ChapterId = "ch08";

    private Control hotspotsRoot;
    private Label hintLabel;
    private Button pourBtn;
    private Button phoneBtn;
    private Control pourGlow;
    private Control phoneGlow;
    private DrinkPour drink;
    private Texture2D stallBg;              // 夜摊原背景（河边开场用）

    private int pourCount;      // 已经倒了几杯（0~4）
    private bool pourBusy;      // 倒酒动画播放中
    private bool finished;      // 尾声播放中
    private bool chatOpen;      // 手机界面是否开着
    private bool deleteWaiting; // 第十九轮：删除引导阶段——四杯下肚，手机在桌上，等玩家做个了断

    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 12000;

    protected override void OnChapterReady()
    {
        GD.Print("第八章：放过自己 —— 酒过三巡，才敢说的话");

        AudioManager.Instance?.PlayBgm(AudioManager.BgmGym3, -14f, 1.5f);

        hotspotsRoot = GetNode<Control>("Hotspots");
        hintLabel = GetNode<Label>("HintLabel");
        pourBtn = GetNode<Button>("Hotspots/PourBtn");
        phoneBtn = GetNode<Button>("Hotspots/PhoneBtn");
        drink = GetNode<DrinkPour>("Drink");

        pourBtn.Pressed += OnPourPressed;
        phoneBtn.Pressed += OnPhonePressed;
        pourGlow = HotspotGlow.Attach(pourBtn);
        phoneGlow = HotspotGlow.Attach(phoneBtn);

        // 第十五轮：热点按"美术图坐标"登记——窗口不成 16:9 时背景会整体偏移，热点跟着走
        ArtAnchor.Track(pourBtn, new Rect2(205f, 675f, 260f, 275f));  // 酒瓶 + 两只杯子那一带
        ArtAnchor.Track(phoneBtn, new Rect2(485f, 812f, 152f, 100f)); // 桌上的手机（已贴进夜摊图）

        // "点万物有回应"：灯串、烤炉的白烟、一把空着的红塑料凳
        AddChild(FlavorSpots.Create(hotspotsRoot, ChapterId));

        // "手机活起来"：手机热点右上角挂未读小红点
        LiveEvents.AttachPhoneDot(phoneBtn);

        // 烤炉上空的火星子（挂在烤炉中上方的位置）——也跟着美术图钉住
        var embers = new EmberDust();
        AddChild(embers);
        ArtAnchor.Track2D(drink, new Vector2(332f, 892f), 0.38f); // 杯底中心钉在夜摊桌面上（第二十四轮 F27：下移坐进桌布前缘，略缩小减少顶视违和）
        ArtAnchor.Track2D(embers, new Vector2(723f, 652f));

        UiSounds.WireAll(this);

        // 开场：先只有对话，热点等 intro 播完再放出来
        hotspotsRoot.Visible = false;
        hintLabel.Visible = false;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0f);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0f);

        // 第三轮：河边开场——先把他一个人放在黄昏的河堤上，想完事情再去夜摊。
        var bg = GetNodeOrNull<TextureRect>("Background");
        if (bg != null)
        {
            stallBg = bg.Texture;
            if (ResourceLoader.Exists("res://assets/art/backgrounds/river_dusk_v1.png"))
                bg.Texture = GD.Load<Texture2D>("res://assets/art/backgrounds/river_dusk_v1.png");
        }
        DialogueManager.Instance.PlaySequence(ChapterId, "river_intro", () =>
        {
            // 第三轮：二十秒内不选，也是答案——就坐着看河水
            DialogueManager.Instance.ShowChoice(ChapterId, "river", OnRiverChosen, 20f, OnRiverTimeout);
        });
    }

    /// <summary>河边的「不选」——比任何选项都真实的那种回答</summary>
    private void OnRiverTimeout()
    {
        GameManager.Instance.AddCourage(4); // 沉默也是勇气
        DialogueManager.Instance.PlaySequence(ChapterId, "river_wait", BackToNightStall);
    }

    /// <summary>河边的三个反思方向（第三轮）</summary>
    private void OnRiverChosen(int index)
    {
        GameManager.Instance.AddCourage(index == 0 ? 5 : index == 1 ? 3 : 2);
        string seq = index switch { 0 => "river_clear", 1 => "river_stuck", _ => "river_none" };
        DialogueManager.Instance.PlaySequence(ChapterId, seq, BackToNightStall);
    }

    /// <summary>想完了 → 背景淡切回夜摊 → 接原 intro</summary>
    private void BackToNightStall()
    {
        if (drink != null) drink.Visible = true;
        var bg = GetNodeOrNull<TextureRect>("Background");
        if (bg == null || stallBg == null)
        {
            DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished);
            return;
        }
        var tw = CreateTween();
        tw.TweenProperty(bg, "modulate:a", 0f, 1.2);
        tw.TweenCallback(Callable.From(() =>
        {
            bg.Texture = stallBg;
            var tw2 = CreateTween();
            tw2.TweenProperty(bg, "modulate:a", 1f, 1.4);
            tw2.TweenCallback(Callable.From(() =>
                DialogueManager.Instance.PlaySequence(ChapterId, "intro", OnIntroFinished)));
        }));
    }

    /// <summary>发呆 12 秒 → 酒瓶子轻轻呼吸一下（提醒还能倒酒）</summary>
    public override void _Process(double delta)
    {
        base._Process(delta);
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

        if (deleteWaiting)
        {
            // 删除引导阶段发呆：该提醒的是手机，不是酒瓶
            if (phoneGlow != null)
                HotspotGlow.Pulse(phoneGlow, 0f);
        }
        else if (pourCount < 4 && pourGlow != null)
        {
            HotspotGlow.Pulse(pourGlow, 0f);
        }
    }

    private void OnIntroFinished()
    {
        hotspotsRoot.Visible = true;
        hintLabel.Visible = true;
        lastActivityMsec = Time.GetTicksMsec();
        UpdateHint();

        // "手机活起来"：酒过三巡之前，妈发来一句"天凉了"
        LiveEvents.Fire(this, ChapterId, "ch08_ma_cold");
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
            hintLabel.Text = DataStore.Text("hint.ch08", pourCount, 4);
            return;
        }

        hintLabel.Text = DataStore.Text("hint.ch08_done");
    }

    // ==================== 倒酒 ====================

    private void OnPourPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || chatOpen || finished || pourBusy)
            return;

        // 四杯已满 + 删除引导中：点酒瓶 = "先把这口喝完，不急着删"——走跳过分支，不卡玩家
        if (pourCount >= 4)
        {
            if (!deleteWaiting)
                return;

            deleteWaiting = false;
            lastActivityMsec = Time.GetTicksMsec();
            HotspotGlow.Sparkle(pourBtn);
            dm.PlaySequence(ChapterId, "delete_skip", StartReplyChoice);
            return;
        }

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

    /// <summary>四杯酒下肚——先掏手机，再轮到那句话（第十九轮：删除引导）</summary>
    private void OnPoursDone()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "delete_lead", () =>
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            deleteWaiting = true;
            lastActivityMsec = Time.GetTicksMsec();

            // 引导：提示语换成"做个了断"，手机热点开始呼吸——把选择权完整交给玩家
            hintLabel.Text = DataStore.Text("hint.ch08_delete");
            hintLabel.Visible = true;
            if (phoneGlow != null)
                HotspotGlow.Pulse(phoneGlow, 0f);
        });
    }

    /// <summary>删除引导结束（删了 / 跳过）→ 回到正轨：那句"怎么接住"的三选一</summary>
    private void StartReplyChoice()
    {
        deleteWaiting = false;
        hintLabel.Visible = false;
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
            // 第二十四轮 F35：碰杯是这一章的情绪转折——苦涩（Gym3）从这一口起转成释然（Piano Between）
            AudioManager.Instance?.PlayBgm(AudioManager.BgmPianoBetween, -14f, 2.5f);
            DialogueManager.Instance.PlaySequence(ChapterId, "outro", () =>
            {
                // 第十九轮保底（F16）：玩家跳过了删除引导也没关系——剧情上"回去的路上"这一刀照样落。
                // 第 7 章起列表里不再有宝宝，第 9 章《置顶》时列表才是干净的。
                // 已经亲手删过的话，DeleteFriend 幂等，不会重复落档。
                GameManager.Instance?.DeleteFriend("baobao", "ch08_auto_delete");

                AudioManager.Instance?.PlayBgm(AudioManager.BgmGym1, -14f, 2.5f);
                GoToNextChapter("res://scenes/chapters/ch09/ch09.tscn", 9);
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

            // 第十九轮：删除引导阶段关掉微信——检查那一刀落了没有
            if (deleteWaiting)
            {
                bool deleted = GameManager.Instance?.IsDeleted("baobao") == true;
                if (deleted)
                {
                    // 亲手删完了。给一段收尾对白，然后回到三选一
                    DialogueManager.Instance.PlaySequence(ChapterId, "delete_done", StartReplyChoice);
                }
                else
                {
                    // 还没删。不催第二遍，只让酒桌陪玩家安静一下——手机还可以再点
                    DialogueManager.Instance.PlaySequence(ChapterId, "delete_notyet", () =>
                    {
                        deleteWaiting = true;
                        lastActivityMsec = Time.GetTicksMsec();
                        hintLabel.Text = DataStore.Text("hint.ch08_delete");
                        hintLabel.Visible = true;
                        if (phoneGlow != null)
                            HotspotGlow.Pulse(phoneGlow, 0f);
                    });
                }
            }
        };
        overlay.Open(""); // 空字符串 = 掏手机：先落在锁屏，未读通知卡上能看见妈那条消息，点它进微信
    }
}
