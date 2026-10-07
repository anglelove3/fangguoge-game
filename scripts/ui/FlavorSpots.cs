using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// "点万物有回应"（第十二轮）
///
/// 【这是什么】
/// 每个章节的场景里，除了主线热点之外，还藏着 2~3 处"没任务、没奖励"的小地方：
/// 窗边的月亮、柜门上的照片、桌上的泡面……点一下，主角会冒一句心里话。
/// 不看也不会卡关，看了就像在场景里多待了一会儿——这是给"喜欢乱点"的玩家的礼物。
///
/// 【数据在哪】
/// data/flavor/{章节id}.json：每个点带 id、坐标（0~1 的比例）、是否只看一次、要说的话。
/// 改文案、调位置都不用碰代码。
///
/// 【交互约定】
///   - 隐形按钮（和主线热点同一套：悬停柔光、点击星光、没有框）；
///   - 对话进行中 / 手机开着 → 点了也不响应（和其他热点一致）；
///   - 发呆 20 秒 → 还没点过的小地方轻轻眨一下光（害羞地提醒你它在这儿）。
/// </summary>
public partial class FlavorSpots : Control
{
    private class Spot
    {
        public string Id = "";
        public bool Once = true;
        public List<DialogueLine> Lines = new();
        public Button Btn;
        public Control Glow;
        public bool Used;
    }

    private readonly List<Spot> spots = new();
    private ulong lastActivityMsec;
    private const ulong IdleHintDelayMsec = 20000;

    /// <summary>
    /// 数据文件结构：{ "spots": [ { id, x, y, w, h, once, lines:[{speaker,text}] } ] }
    /// x/y/w/h 都是 0~1 的比例（相对整个屏幕）
    /// </summary>
    private class FlavorFile
    {
        public List<FlavorSpotEntry> Spots { get; set; } = new();
    }

    private class FlavorSpotEntry
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; } = 0.08f;
        public float H { get; set; } = 0.08f;
        public bool Once { get; set; } = true;
        public List<DialogueLine> Lines { get; set; } = new();
    }

    /// <summary>
    /// 给章节挂上"点万物有回应"。挂完返回这个节点（挂着发呆提示的计时器）。
    /// 章节在 OnChapterReady 里调用：
    ///   AddChild(FlavorSpots.Create(hotspotsRoot, "ch01"));
    /// </summary>
    public static FlavorSpots Create(Control hotspotsRoot, string chapterId)
    {
        var host = new FlavorSpots();
        host.Name = "FlavorSpots";

        var file = DataStore.LoadJson<FlavorFile>($"res://data/flavor/{chapterId}.json");
        if (file == null || file.Spots.Count == 0)
            return host; // 没配数据就静静地什么都不做

        foreach (var def in file.Spots)
        {
            var spot = new Spot { Id = def.Id, Once = def.Once, Lines = def.Lines };

            var btn = new Button
            {
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                Flat = true,
                FocusMode = FocusModeEnum.None,
            };
            // 比例 → 锚点（跟随窗口缩放，和主线热点同一套做法）
            btn.AnchorLeft = def.X;
            btn.AnchorTop = def.Y;
            btn.AnchorRight = def.X + def.W;
            btn.AnchorBottom = def.Y + def.H;
            btn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            spot.Btn = btn;
            spot.Glow = HotspotGlow.Attach(btn);
            btn.Pressed += () => host.OnSpotPressed(spot);

            hotspotsRoot.AddChild(btn);
            host.spots.Add(spot);
        }

        return host;
    }

    private void OnSpotPressed(Spot spot)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed)
            return; // 对话中 / 手机开着：不理
        if (spot.Once && spot.Used)
            return;
        if (spot.Lines == null || spot.Lines.Count == 0)
            return;

        spot.Used = true;
        lastActivityMsec = Time.GetTicksMsec();

        HotspotGlow.Sparkle(spot.Btn);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -14f, 0.05f);

        dm.PlayLines(spot.Lines);
    }

    /// <summary>发呆 20 秒 → 让还没点过的小地方轻轻呼吸（其他热点有自己的 12 秒节拍，各闪各的）</summary>
    public override void _Process(double delta)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || dm.IsBusy || dm.IsUiSuppressed)
        {
            lastActivityMsec = Time.GetTicksMsec();
            return;
        }

        if (Time.GetTicksMsec() - lastActivityMsec < IdleHintDelayMsec)
            return;

        lastActivityMsec = Time.GetTicksMsec();

        float delay = 0f;
        foreach (var spot in spots)
        {
            if (spot.Once && spot.Used)
                continue;
            if (!GodotObject.IsInstanceValid(spot.Btn) || !spot.Btn.IsVisibleInTree())
                continue;
            HotspotGlow.Pulse(spot.Glow, delay);
            delay += 0.35f;
        }
    }
}
