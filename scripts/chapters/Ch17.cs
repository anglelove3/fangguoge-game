using Godot;

/// <summary>
/// 第十七章：后来
///
/// 所有路径都走到同一件事：他靠自己的准备，到了说好的城市；
/// 几年后是一间有阳台的小屋，一张桌子，两把吉他，一串珠，一只猫。
/// 第十六章那五个字发没发出去，只改她记得的方式。
/// 好感与勇气只改最后那一句的语气，不改他们有没有见面。
/// </summary>
public partial class Ch17 : ChapterBase
{
    private const string ChapterId = "ch17";

    private TextureRect home;
    private Control flavor;
    private bool finished;

    protected override void OnChapterReady()
    {
        GD.Print("第十七章：后来 —— 谁也不用放下自己的事。");

        home = GetNode<TextureRect>("Home");
        var hotspots = GetNode<Control>("Hotspots");
        flavor = MakeFlavorLayer(hotspots, "ch17_home");
        flavor.Visible = false;

        UiSounds.WireAll(this);
        StoryClock.ClearSegment();

        var tw = CreateTween();
        tw.SetParallel(true);
        if (titleLabel != null)
            tw.TweenProperty(titleLabel, "modulate:a", 0.0f, 1.8f);
        if (subtitleLabel != null)
            tw.TweenProperty(subtitleLabel, "modulate:a", 0.0f, 1.8f);
        tw.Chain().TweenCallback(Callable.From(StartMeet));
    }

    private Control MakeFlavorLayer(Control hotspotsRoot, string flavorId)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hotspotsRoot.AddChild(layer);
        AddChild(FlavorSpots.Create(layer, flavorId));
        return layer;
    }

    private void StartMeet()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "meet", PlaySaid);
    }

    private void PlaySaid()
    {
        int idx = GameManager.Instance.GetChoice("ch16_final");
        string seq = idx switch
        {
            0 => "said_sent",
            1 => "said_soft",
            2 => "said_deleted",
            _ => "said_quiet",
        };
        DialogueManager.Instance.PlaySequence(ChapterId, seq, ShowHome);
    }

    private void ShowHome()
    {
        var tw = CreateTween();
        tw.TweenProperty(home, "modulate:a", 1.0f, 1.6f)
          .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.TweenCallback(Callable.From(() =>
        {
            flavor.Visible = true;
            DialogueManager.Instance.PlaySequence(ChapterId, "home", PlayTone);
        }));
    }

    private void PlayTone()
    {
        string seq = GameManager.Instance.GetEndingType() switch
        {
            EndingType.SunnyDay => "tone_sunny",
            EndingType.SouthWind => "tone_south",
            EndingType.NowIUnderstand => "tone_understand",
            _ => "tone_slow",
        };
        DialogueManager.Instance.PlaySequence(ChapterId, seq, PlayCard);
    }

    private void PlayCard()
    {
        DialogueManager.Instance.PlaySequence(ChapterId, "card", FinishChapter);
    }

    private void FinishChapter()
    {
        if (finished)
            return;
        finished = true;
        GameManager.Instance.RecordRelationshipEvent("jie", "ch17_home");
        GoToNextChapter("res://scenes/ui/demo_end/demo_end.tscn", 99);
    }

    public override void _ExitTree()
    {
        StoryClock.ClearSegment();
        base._ExitTree();
    }
}
