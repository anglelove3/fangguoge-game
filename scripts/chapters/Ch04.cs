using Godot;
using System.Collections.Generic;

/// <summary>
/// 第四章：星月（正式分手章，第三轮立项）
///
/// 电话里那句「对不起」之后，他跑到星月广场。她站在喷泉边上，没回头。
/// 以前分过一次又复合过——这次是真的。
///
/// 【流程】
/// 开场独白 → 热点探索（喷泉/长椅/灯柱）→ 见她 → 拉扯演出（TugOfWar）→
/// 心声三选一（再抱一下/问一个问题/看着她走）→ 放手长镜头 → 尾声 → 接《晚安》
///
/// 【拉扯演出】她说要走 → TugOfWar：按住=抓住手腕，追不上=滑脱，松开=主动放手。
/// 两条路同一个结局，演出文本不同。主动松开勇气 +4，滑脱 +2。
///
/// 【放手长镜头】outro 前背景缓拉远（Scale 1.0→0.94），月亮变大，只剩喷泉声。
/// </summary>
public partial class Ch04 : ChapterBase
{
    private const string ChapterId = "ch04"; // 对应 data/dialogues/ch04.json
    private Button fountainBtn, gateBtn, lampBtn, herBtn;
    private Label hintLabel;
    private Control hotspotsRoot;
    private readonly HashSet<string> foundItems = new();
    private bool meetDone;
    private bool tugDone;
    private bool finished;

    public override void _Ready()
    {
        try
        {
        base._Ready();

        hintLabel = GetNode<Label>("HintLabel");
        hotspotsRoot = GetNode<Control>("Hotspots");
        fountainBtn = GetNode<Button>("Hotspots/FountainBtn");
        gateBtn = GetNode<Button>("Hotspots/GateBtn");
        lampBtn = GetNode<Button>("Hotspots/LampBtn");
        herBtn = GetNode<Button>("Hotspots/HerBtn");

        fountainBtn.Pressed += () => OnHotspot("fountain", fountainBtn);
        gateBtn.Pressed += () => OnHotspot("gate", gateBtn);
        lampBtn.Pressed += () => OnHotspot("lamp", lampBtn);
        herBtn.Pressed += OnHerPressed;

        // 热点按美术图钉住
        ArtAnchor.TrackFraction(fountainBtn, new Rect2(0.22f, 0.18f, 0.45f, 0.58f));
        ArtAnchor.TrackFraction(gateBtn, new Rect2(0.74f, 0.18f, 0.22f, 0.55f));
        ArtAnchor.TrackFraction(lampBtn, new Rect2(0.02f, 0.22f, 0.16f, 0.45f));
        ArtAnchor.TrackFraction(herBtn, new Rect2(0.52f, 0.53f, 0.13f, 0.24f));
        
        UiSounds.WireAll(this);


        // 标题淡出
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(titleLabel, "modulate:a", 0.0f, 2.0);
        tween.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 2.0);

        // 第三轮 F6 修复：intro 播完必须放开热点——旧版回调传 null，
        // hotspotsRoot 永远不可见，玩家点哪儿都没反应（整个章卡死在开场）
        DialogueManager.Instance.PlaySequence(ChapterId, "intro", () =>
        {
            hotspotsRoot.Visible = true;
            hintLabel.Visible = true;
        });
        GD.Print("[星月] _Ready 完成，热点已放开");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[星月] _Ready 异常: {e}");
        }
    }

    private void OnHotspot(string id, Button btn)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || foundItems.Contains(id))
            return;
        foundItems.Add(id);
        HotspotGlow.Sparkle(btn);
        btn.Disabled = true;
        dm.PlaySequence(ChapterId, $"explore_{id}", () =>
        {
            if (foundItems.Count >= 3 && !meetDone)
            {
                dm.PlaySequence(ChapterId, "all_found", null);
                hintLabel.Text = "（她在喷泉边。点她走过去。）";
            }
        });
    }

    /// <summary>见她 → 拉扯演出</summary>
    private void OnHerPressed()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || meetDone)
            return;
        meetDone = true;
        herBtn.Visible = false;
        HotspotGlow.Sparkle(herBtn);

        dm.PlaySequence(ChapterId, "meet", () =>
        {
            // 第三轮：她视角的 13 秒——他走过去之前，她的手机震过。
            // 那条消息他没看见。谁发的、她为什么没回——不解释。
            AddChild(new HerPovMoment(() =>
            {
            // 拉扯演出：抓住她的手腕
            AddChild(new TugOfWar(released =>
            {
                tugDone = true;
                // 主动松开勇气 +4（放手的人更勇敢），滑脱 +2
                GameManager.Instance.AddCourage(released ? 4 : 2);
                string seq = released ? "tug_release" : "tug_slip";
                DialogueManager.Instance.PlaySequence(ChapterId, seq, OnTugResolved);
            }));
            }));
        });
    }

    /// <summary>拉扯落幕 → 余波 → 心声三选一 → 放手长镜头 → 尾声</summary>
    private void OnTugResolved()
    {
        var dm = DialogueManager.Instance;
        dm.PlaySequence(ChapterId, "aftermath", () =>
        {
            dm.PlaySequence(ChapterId, "outro", () =>
            {
                if (finished)
                    return;
                finished = true;

                // 放手长镜头：背景缓缓拉远，月亮变大——他越来越小，广场越来越大
                var bg = GetNodeOrNull<TextureRect>("Background");
                if (bg != null)
                {
                    bg.PivotOffset = GetViewportRect().Size / 2f;
                    var drift = CreateTween();
                    drift.SetParallel(true);
                    drift.TweenProperty(bg, "scale", new Vector2(0.94f, 0.94f), 18.0)
                        .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                }
                // BGM 停 3 秒（只剩喷泉的环境音），再接下一章
                AudioManager.Instance?.PlayBgm("", 0f, 3.0f);

                GetTree().CreateTimer(5.0).Timeout += () =>
                {
                    GoToNextChapter("res://scenes/chapters/ch05/ch05.tscn", 5);
                };
            });
        });
    }
}
