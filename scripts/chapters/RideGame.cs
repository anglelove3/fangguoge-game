using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 骑行小游戏（第十章《向南》招牌，纯代码绘制——天平 / 倒酒 / 台球同一条路线）
///
/// 【这是什么】
/// 横版卷轴：道路向前，路牌、里程碑、田野、电线杆向后掠；左下电量条，右下里程表。
/// 玩家只做一件事——**按住鼠标 = 加速（耗电），松开 = 滑行（微回电）**。
/// 在两个充电点之间把电管住，就是这一段的全部玩法。
///
/// 【为叙事让路】
/// - 台词框出现时车自动滑行（点击都归对白，不打架——空格是"继续对话"，所以加速只认鼠标按住）；
/// - 里程碑路牌掠过时可点（点一下，一句"路过的话"），不点不卡流程；
/// - 电量见底不失败：推车走，慢慢"搭上电"，剧情照走；
/// - 到充电点 / 剧情点（株洲出口、下雨抉择）自动停靠，Ch10 接手演出。
///
/// 【给自动化回归留的口子】
/// OdoLabel / BatteryLabel 是具名节点可断言；KmNow / BatteryNow / AccelNow 公开读；
/// ActiveMilestoneInfo() 返回当前可点路牌的 id 和屏幕矩形（驱动用它算点击坐标）。
/// </summary>
public partial class RideGame : Control
{
    // ==================== 配置 ====================

    public class MilestoneDef
    {
        public float Km { get; set; }        // 段内公里数（可点击窗口的前沿）
        public string Id { get; set; } = ""; // 点了之后 Ch10 播哪段
        public string Label { get; set; } = ""; // 牌子上写的地名
    }

    public class AutoEventDef
    {
        public float Km { get; set; }
        public string Id { get; set; } = ""; // 到点自动停靠（株洲出口 / 下雨抉择）
    }

    public class RideConfig
    {
        public float TotalKm { get; set; }               // 本段里程
        public float OdoStart { get; set; }              // 段起点时"距临澜"还剩多少（里程表显示用）
        public bool StartRain { get; set; }              // 起手就是雨段吗（第 2 段后半才下，靠 SetRain 切）
        public List<MilestoneDef> Milestones { get; set; } = new();
        public List<AutoEventDef> AutoEvents { get; set; } = new();
        public List<float> ChargeKms { get; set; } = new(); // 到点自动停靠充电（按顺序回调）
    }

    // ==================== 事件 ====================

    /// <summary>点了一个还没错过的里程碑（参数 = id）</summary>
    public Action<string> MilestonePressed;
    /// <summary>骑到充电点（参数 = 第几个，按配置顺序）——游戏自动停，Ch10 接手演出</summary>
    public Action<int> ChargeArrived;
    /// <summary>到剧情点（株洲出口 / 下雨抉择）——游戏自动停，Ch10 接手演出</summary>
    public Action<string> AutoEventReached;
    /// <summary>电量见底（本段最多一次）——已经自动进入推车兜底，Ch10 只管播一句台词</summary>
    public Action BatteryEmpty;
    /// <summary>骑完全段</summary>
    public Action Finished;

    // ==================== 状态 ====================

    private RideConfig cfg;
    private bool running;      // _Process 里是否推进（Pause/Resume 开关）
    private bool active;       // 是否在游戏中（隐藏时不画不收输入）
    private bool holdAccel;    // 鼠标按住 = 想加速
    private bool pushing;      // 电量见底 → 推车兜底
    private bool batteryEmptyFired;
    private int chargeIdx;     // 下一个充电点序号
    private int autoIdx;       // 下一个剧情点序号
    private float kmDone;      // 段内已骑公里数
    private float battery = 100f;
    private float rainNow;     // 当前雨强度（0~1，向 rainTarget 渐变）
    private float rainTarget;
    private float rideTime;    // 本段骑了多久（秒，提示条淡出用）
    private float dialogCoast; // >0：对白期间强制滑行的剩余秒（防抖）

    // 调参（一段 90km 全加速约 55 秒、耗电 125%——硬冲必没电，逼出"松一把"的节奏）
    private const float SpeedAccel = 1.65f;   // km/s
    private const float SpeedCoast = 0.78f;
    private const float SpeedPush = 0.45f;    // 推车
    private const float DrainAccel = 2.3f;    // %/s（全加速 1.39%/km：60km 耗 84%，140km 耗 195%）
    private const float RegenCoast = 0.5f;    // %/s（松一半的电，刚好能骑完两个充电点之间）
    private const float PushRecover = 0.6f;   // 推车时缓慢"搭电" %/s
    private const float PushResumeAt = 18f;   // 搭到 18% 跨上车

    // 画面布局（逻辑 1920×1080）
    private const float BikeX = 430f;
    private const float HorizonY = 400f;
    private const float RoadTop = 620f;
    private const float RoadBottom = 960f;
    private const float RoadMidY = 800f;      // 车轮压的线
    private const float PxNear = 1150f;       // 近景（路牌/电线杆）每公里多少像素

    // 天空配色（当前值向目标渐变）
    private Color skyTop = new(0.45f, 0.62f, 0.78f);
    private Color skyHorizon = new(0.92f, 0.85f, 0.70f);
    private static readonly Color AmTop = new(0.45f, 0.62f, 0.78f);
    private static readonly Color AmHorizon = new(0.93f, 0.86f, 0.71f);
    private static readonly Color PmTop = new(0.55f, 0.58f, 0.62f);
    private static readonly Color PmHorizon = new(0.83f, 0.81f, 0.75f);
    private static readonly Color RainTop = new(0.30f, 0.33f, 0.38f);
    private static readonly Color RainHorizon = new(0.56f, 0.59f, 0.61f);

    // ==================== 节点（具名，驱动可断言） ====================

    private Label odoLabel;
    private Label batteryLabel;
    private Label hintLabel;
    private ColorRect batteryFill;
    private ColorRect batteryFrame;

    private readonly List<Button> milestoneBtns = new(); // 当前可点的路牌

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 骑行时接住按住加速
        Visible = false;

        // ---- 里程表（右下） ----
        odoLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Text = "",
        };
        odoLabel.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        odoLabel.AddThemeFontSizeOverride("font_size", 26);
        odoLabel.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.88f));
        odoLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.12f, 0.9f));
        odoLabel.AddThemeConstantOverride("outline_size", 8);
        odoLabel.AnchorLeft = 1f; odoLabel.AnchorRight = 1f;
        odoLabel.AnchorTop = 1f; odoLabel.AnchorBottom = 1f;
        odoLabel.OffsetLeft = -430; odoLabel.OffsetRight = -32;
        odoLabel.OffsetTop = -74; odoLabel.OffsetBottom = -30;
        odoLabel.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(odoLabel);

        // ---- 电量条（左下：边框 + 填充 + 文字） ----
        batteryFrame = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(1f, 0.96f, 0.88f, 0.85f),
        };
        batteryFrame.AnchorTop = 1f; batteryFrame.AnchorBottom = 1f;
        batteryFrame.OffsetLeft = 32; batteryFrame.OffsetRight = 336;
        batteryFrame.OffsetTop = -58; batteryFrame.OffsetBottom = -24;
        AddChild(batteryFrame);

        batteryFill = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = new Color(0.55f, 0.78f, 0.45f) };
        batteryFrame.AddChild(batteryFill);

        batteryLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Text = "电量 100%",
        };
        batteryLabel.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        batteryLabel.AddThemeFontSizeOverride("font_size", 24);
        batteryLabel.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.88f));
        batteryLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.12f, 0.9f));
        batteryLabel.AddThemeConstantOverride("outline_size", 8);
        batteryLabel.AnchorTop = 1f; batteryLabel.AnchorBottom = 1f;
        batteryLabel.OffsetLeft = 32; batteryLabel.OffsetRight = 340;
        batteryLabel.OffsetTop = -100; batteryLabel.OffsetBottom = -62;
        AddChild(batteryLabel);

        // ---- 操作提示（中下，起步几秒后淡出） ----
        hintLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Text = "按住鼠标：加速 · 松开：滑行",
        };
        hintLabel.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
        hintLabel.AddThemeFontSizeOverride("font_size", 24);
        hintLabel.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.88f, 0.95f));
        hintLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.12f, 0.9f));
        hintLabel.AddThemeConstantOverride("outline_size", 8);
        hintLabel.AnchorLeft = 0.5f; hintLabel.AnchorRight = 0.5f;
        hintLabel.AnchorTop = 1f; hintLabel.AnchorBottom = 1f;
        hintLabel.OffsetLeft = -280; hintLabel.OffsetRight = 280;
        hintLabel.OffsetTop = -150; hintLabel.OffsetBottom = -110;
        hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(hintLabel);
    }

    /// <summary>开一段路。配置见 RideConfig；事件回调要在 Start 前挂好。</summary>
    public void Start(RideConfig config)
    {
        cfg = config;
        kmDone = 0f;
        battery = 100f;
        running = true;
        active = true;
        pushing = false;
        batteryEmptyFired = false;
        chargeIdx = 0;
        autoIdx = 0;
        rideTime = 0f;
        holdAccel = false;
        rainNow = cfg.StartRain ? 1f : 0f;
        rainTarget = rainNow;

        Visible = true;
        Modulate = new Color(1, 1, 1, 1);
        hintLabel.Modulate = new Color(1, 1, 1, 1);
        ClearMilestoneButtons();
        UpdateHud();
        QueueRedraw();
    }

    /// <summary>停靠演出（充电 / 剧情点）期间暂停推进；画面保持。
    /// 顺手把"按住"清掉：点路牌的那下按下/松开都可能被按钮吃掉，不清会一直加速。</summary>
    public void Pause()
    {
        running = false;
        holdAccel = false;
    }

    public void Resume() => running = true;

    /// <summary>雨强度目标（0~1）。停下躲雨 / 雨变小的时候 Ch10 调它。</summary>
    public void SetRain(float target) => rainTarget = Mathf.Clamp(target, 0f, 1f);

    /// <summary>收摊（进下一幕前调用）：隐藏 + 清路牌。</summary>
    public void End()
    {
        active = false;
        running = false;
        Visible = false;
        ClearMilestoneButtons();
    }

    public override void _ExitTree()
    {
        MilestonePressed = null;
        ChargeArrived = null;
        AutoEventReached = null;
        BatteryEmpty = null;
        Finished = null;
    }

    // ==================== 驱动断言口子 ====================

    public float KmNow => kmDone;
    public float BatteryNow => battery;
    public bool AccelNow => holdAccel && !IsDialogueBusy();
    public string OdoText => odoLabel?.Text ?? "";
    public string BatteryText => batteryLabel?.Text ?? "";

    /// <summary>当前屏幕上可点的路牌（id + 全局矩形）；没有就是空字典。</summary>
    public Godot.Collections.Dictionary ActiveMilestoneInfo()
    {
        foreach (var btn in milestoneBtns)
        {
            if (GodotObject.IsInstanceValid(btn) && btn.IsVisibleInTree())
                return new Godot.Collections.Dictionary
                {
                    { "id", (string)btn.GetMeta("milestone_id") },
                    { "rect", btn.GetGlobalRect() },
                };
        }
        return new Godot.Collections.Dictionary();
    }

    // ==================== 输入 ====================

    public override void _GuiInput(InputEvent @event)
    {
        if (!active || !running)
            return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            holdAccel = mb.Pressed;
            AcceptEvent();
        }
    }

    /// <summary>对白框正忙着（打字 / 等点击）——车自动滑行，点击都归对白。</summary>
    private bool IsDialogueBusy()
    {
        var dm = DialogueManager.Instance;
        return dm != null && (dm.IsBusy || dm.IsUiSuppressed);
    }

    // ==================== 推进 ====================

    public override void _Process(double delta)
    {
        if (!active || !running)
            return;

        float dt = (float)delta;
        rideTime += dt;

        // 对白期间：强制滑行 0.4s 防抖（台词一句接一句时不会一冲一停）
        bool dialogueBusy = IsDialogueBusy();
        if (dialogueBusy)
            dialogCoast = 0.4f;
        else if (dialogCoast > 0f)
            dialogCoast -= dt;
        bool accel = holdAccel && !pushing && !dialogueBusy && dialogCoast <= 0f;

        // 电量
        if (pushing)
        {
            battery = Mathf.Min(PushResumeAt, battery + PushRecover * dt);
            if (battery >= PushResumeAt)
            {
                pushing = false; // 搭上电，跨上车
            }
        }
        else if (accel)
        {
            battery -= DrainAccel * dt;
            if (battery <= 0f)
            {
                battery = 0f;
                pushing = true;
                if (!batteryEmptyFired)
                {
                    batteryEmptyFired = true;
                    BatteryEmpty?.Invoke(); // Ch10 播"推一段"的台词
                }
            }
        }
        else
        {
            battery = Mathf.Min(100f, battery + RegenCoast * dt);
        }

        // 里程
        float speed = pushing ? SpeedPush : accel ? SpeedAccel : SpeedCoast;
        kmDone += speed * dt;

        // 雨强度渐变
        rainNow = Mathf.MoveToward(rainNow, rainTarget, 0.25f * dt);

        // 天空配色渐变（上午晴 → 下午阴 → 雨）
        Color tTop, tHor;
        if (rainNow > 0.5f) { tTop = RainTop; tHor = RainHorizon; }
        else if (rainNow > 0.05f)
        {
            tTop = PmTop.Lerp(RainTop, rainNow * 2f - 0.1f);
            tHor = PmHorizon.Lerp(RainHorizon, rainNow * 2f - 0.1f);
        }
        else { tTop = PmTop; tHor = PmHorizon; }
        skyTop = skyTop.Lerp(tTop, 0.8f * dt);
        skyHorizon = skyHorizon.Lerp(tHor, 0.8f * dt);

        // 充电点 / 剧情点 / 终点
        if (cfg?.ChargeKms != null && chargeIdx < cfg.ChargeKms.Count && kmDone >= cfg.ChargeKms[chargeIdx])
        {
            Pause();
            ClearMilestoneButtons();
            battery = 100f; // 充上了：电条回满（推车兜底的也一并救回来）
            var cb = ChargeArrived;
            chargeIdx++;
            cb?.Invoke(chargeIdx - 1);
            return;
        }
        if (cfg?.AutoEvents != null && autoIdx < cfg.AutoEvents.Count && kmDone >= cfg.AutoEvents[autoIdx].Km)
        {
            Pause();
            ClearMilestoneButtons();
            var id = cfg.AutoEvents[autoIdx].Id;
            autoIdx++;
            AutoEventReached?.Invoke(id);
            return;
        }
        if (cfg != null && kmDone >= cfg.TotalKm)
        {
            kmDone = cfg.TotalKm;
            Pause();
            ClearMilestoneButtons();
            Finished?.Invoke();
            return;
        }

        UpdateMilestoneButtons();
        UpdateHud();
        QueueRedraw();
    }

    // ==================== 里程碑路牌 ====================

    private void UpdateMilestoneButtons()
    {
        if (cfg == null)
            return;

        // 消掉已错过的
        for (int i = milestoneBtns.Count - 1; i >= 0; i--)
        {
            var btn = milestoneBtns[i];
            float km = (float)btn.GetMeta("milestone_km");
            bool onScreen = ScreenXFor(km) > -180f;
            if (kmDone > km || !onScreen || IsDialogueBusy())
            {
                if (kmDone > km || !onScreen)
                {
                    btn.QueueFree();
                    milestoneBtns.RemoveAt(i);
                }
            }
        }

        // 放出该出现的
        foreach (var m in cfg.Milestones)
        {
            if (kmDone > m.Km)
                continue;
            float sx = ScreenXFor(m.Km);
            if (sx < 1500f && sx > -180f && FindBtn(m.Id) == null)
            {
                var btn = new Button
                {
                    MouseDefaultCursorShape = CursorShape.PointingHand,
                };
                btn.SetMeta("milestone_id", m.Id);
                btn.SetMeta("milestone_km", m.Km);
                btn.SetMeta("milestone_label", m.Label);
                // 样式：路牌底色 + 悬停微亮（点击判定框比牌子大一圈）
                var normal = new StyleBoxFlat
                {
                    BgColor = new Color(0.22f, 0.36f, 0.27f, 0.95f),
                    BorderColor = new Color(0.92f, 0.90f, 0.82f, 0.9f),
                    BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                };
                var hover = new StyleBoxFlat
                {
                    BgColor = new Color(0.28f, 0.45f, 0.33f, 0.97f),
                    BorderColor = new Color(1f, 0.97f, 0.85f),
                    BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                };
                btn.AddThemeStyleboxOverride("normal", normal);
                btn.AddThemeStyleboxOverride("hover", hover);
                btn.AddThemeStyleboxOverride("pressed", normal);
                btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                btn.AddThemeFontOverride("font", ChoicePanel.GetChoiceFont());
                btn.AddThemeFontSizeOverride("font_size", 20);
                btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.94f, 0.86f));
                btn.Text = m.Label;
                var id = m.Id;
                btn.Pressed += () =>
                {
                    btn.QueueFree();
                    milestoneBtns.Remove(btn);
                    MilestonePressed?.Invoke(id);
                };
                AddChild(btn);
                PositionMilestoneButton(btn);
                milestoneBtns.Add(btn);
            }
        }
    }

    private void PositionMilestoneButton(Button btn)
    {
        float sx = ScreenXFor((float)btn.GetMeta("milestone_km"));
        btn.Position = new Vector2(sx - 80f, RoadTop - 210f);
        btn.Size = new Vector2(160f, 74f);
    }

    private Button FindBtn(string id)
    {
        foreach (var b in milestoneBtns)
        {
            if (GodotObject.IsInstanceValid(b) && (string)b.GetMeta("milestone_id") == id)
                return b;
        }
        return null;
    }

    private void ClearMilestoneButtons()
    {
        foreach (var b in milestoneBtns)
        {
            if (GodotObject.IsInstanceValid(b))
                b.QueueFree();
        }
        milestoneBtns.Clear();
    }

    private float ScreenXFor(float worldKm) => BikeX + (worldKm - kmDone) * PxNear;

    // ==================== HUD ====================

    private void UpdateHud()
    {
        float left = Mathf.Round(cfg.OdoStart - kmDone);
        odoLabel.Text = $"距临澜 {System.Math.Max(left, 0)} km";
        batteryLabel.Text = pushing ? "没电了，推一会儿" : $"电量 {System.Math.Round(battery)}%";
        batteryFill.Color = battery < 25f
            ? new Color(0.80f, 0.38f, 0.30f)
            : battery < 50f
                ? new Color(0.85f, 0.66f, 0.32f)
                : new Color(0.55f, 0.78f, 0.45f);

        // 填充条：随电量缩短（内缩 3px 描边）
        float frac = Mathf.Clamp(battery / 100f, 0f, 1f);
        var fr = batteryFrame.Size;
        batteryFill.Position = new Vector2(3, 3);
        batteryFill.Size = new Vector2(Mathf.Max((fr.X - 6) * frac, 2f), fr.Y - 6);

        // 操作提示骑起来 6 秒后淡走
        if (rideTime > 6f && hintLabel.Modulate.A > 0f)
            hintLabel.Modulate = new Color(1, 1, 1, Mathf.Max(0f, hintLabel.Modulate.A - 0.02f));
    }

    // ==================== 画面（纯代码绘制） ====================

    public override void _Draw()
    {
        if (!active)
            return;
        var vpSize = Size;

        DrawSky(vpSize);
        DrawHills(vpSize);
        DrawFields(vpSize);
        DrawRoad(vpSize);
        DrawRoadside(vpSize);
        DrawSigns();
        DrawBike();
        DrawRain(vpSize);
    }

    private void DrawSky(Vector2 vp)
    {
        const int bands = 12;
        for (int i = 0; i < bands; i++)
        {
            float t0 = (float)i / bands, t1 = (float)(i + 1) / bands;
            var c = skyTop.Lerp(skyHorizon, (t0 + t1) / 2f);
            DrawRect(new Rect2(0, HorizonY * t0, vp.X, HorizonY * (t1 - t0) + 1f), c);
        }
    }

    private void DrawHills(Vector2 vp)
    {
        // 两层远山：越远走得越慢
        DrawHillLayer(vp, 0.10f, HorizonY, 70f, 0.045f, new Color(0.42f, 0.48f, 0.52f, 0.55f));
        DrawHillLayer(vp, 0.22f, HorizonY, 44f, 0.07f, new Color(0.50f, 0.55f, 0.53f, 0.65f));
    }

    private void DrawHillLayer(Vector2 vp, float parallax, float baseY, float amp, float freq, Color col)
    {
        float world = kmDone * PxNear * parallax;
        const float step = 40f;
        var pts = new System.Collections.Generic.List<Vector2> { new(0, baseY) };
        for (float x = 0; x <= vp.X + step; x += step)
        {
            float y = baseY - (Mathf.Sin((x + world) * freq) * 0.6f + Mathf.Sin((x + world) * freq * 2.3f + 1.7f) * 0.4f) * amp - amp * 0.35f;
            // 山脊线不许沉到基线以下：多边形闭合边沿 baseY 横穿，正弦低谷会戳穿它
            // （自交 → 引擎每帧刷 "Invalid polygon data, triangulation failed"）
            y = Mathf.Min(y, baseY - 2f);
            pts.Add(new Vector2(x, y));
        }
        pts.Add(new Vector2(vp.X, baseY));
        DrawColoredPolygon(pts.ToArray(), col);
    }

    private void DrawFields(Vector2 vp)
    {
        // 田野：HorizonY..RoadTop，枯冬色 + 几道深浅条
        DrawRect(new Rect2(0, HorizonY, vp.X, RoadTop - HorizonY), new Color(0.72f, 0.66f, 0.50f));
        float world = kmDone * PxNear * 0.7f;
        var rngCol = new Color(0.65f, 0.59f, 0.44f);
        for (int i = 0; i < 14; i++)
        {
            float seed = i * 137.5f;
            float wx = Mathf.PosMod(seed * 173f - world, vp.X + 400f) - 200f;
            float w = 60f + (i % 4) * 45f;
            DrawRect(new Rect2(wx, HorizonY + 10 + (i % 3) * 55, w, 26), new Color(rngCol, 0.35f));
        }
    }

    private void DrawRoad(Vector2 vp)
    {
        // 路一直铺到屏幕底（RoadBottom 只当"近处路肩"的装饰线用）
        DrawRect(new Rect2(0, RoadTop, vp.X, vp.Y - RoadTop), new Color(0.33f, 0.32f, 0.31f));
        // 路肩线
        DrawRect(new Rect2(0, RoadTop + 10, vp.X, 5), new Color(0.85f, 0.83f, 0.76f, 0.75f));
        DrawRect(new Rect2(0, RoadBottom - 16, vp.X, 5), new Color(0.85f, 0.83f, 0.76f, 0.75f));
        // 中央虚线（跟着里程滚动）
        float dashW = 64f, gap = 96f, period = dashW + gap;
        float off = Mathf.PosMod(kmDone * PxNear, period);
        for (float x = -off; x < vp.X; x += period)
            DrawRect(new Rect2(x, RoadMidY + 44, dashW, 6), new Color(0.90f, 0.87f, 0.78f, 0.7f));

        // 雨天的路面反光
        if (rainNow > 0.3f)
            DrawRect(new Rect2(0, RoadTop, vp.X, RoadBottom - RoadTop), new Color(0.45f, 0.52f, 0.58f, 0.20f * rainNow));
    }

    private void DrawRoadside(Vector2 vp)
    {
        // 电线杆：每 0.5km 一根，在路的远侧
        float spacingKm = 0.5f;
        int first = (int)Mathf.Floor((kmDone - (vp.X - BikeX) / PxNear - 0.2f) / spacingKm);
        int last = (int)Mathf.Ceil((kmDone + BikeX / PxNear + 0.2f) / spacingKm);
        var poleCol = new Color(0.36f, 0.32f, 0.28f);
        var tops = new System.Collections.Generic.List<Vector2>();
        for (int i = first; i <= last; i++)
        {
            float sx = ScreenXFor(i * spacingKm);
            if (sx < -80f || sx > vp.X + 80f)
                continue;
            float topY = RoadTop - 210f;
            DrawRect(new Rect2(sx - 3f, topY, 6f, RoadTop + 20f - topY), poleCol);
            DrawRect(new Rect2(sx - 26f, topY + 14f, 52f, 4f), poleCol);
            tops.Add(new Vector2(sx, topY + 16f));
        }
        if (tops.Count >= 2)
        {
            var wire = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < tops.Count - 1; i++)
            {
                var a = tops[i]; var b = tops[i + 1];
                for (int s = 0; s <= 6; s++)
                {
                    float t = s / 6f;
                    float y = Mathf.Lerp(a.Y, b.Y, t) + Mathf.Sin(t * Mathf.Pi) * 14f; // 垂度
                    wire.Add(new Vector2(Mathf.Lerp(a.X, b.X, t), y));
                }
            }
            DrawPolyline(wire.ToArray(), new Color(0.25f, 0.24f, 0.26f, 0.65f), 2f);
        }

        // 路边树：每 0.13km 一棵（哈希错落），冬天只剩枝干
        float tSpacing = 0.13f;
        int tf = (int)Mathf.Floor((kmDone - (vp.X - BikeX) / PxNear - 0.1f) / tSpacing);
        int tl = (int)Mathf.Ceil((kmDone + BikeX / PxNear + 0.1f) / tSpacing);
        for (int i = tf; i <= tl; i++)
        {
            float sx = ScreenXFor(i * tSpacing + HashOff(i));
            if (sx < -60f || sx > vp.X + 60f)
                continue;
            float h = 70f + (HashOff(i + 91) * 0.5f + 0.5f) * 60f;
            float baseY = RoadTop - 40f - HashOff(i + 7) * 30f;
            DrawLine(new Vector2(sx, baseY), new Vector2(sx + HashOff(i + 3) * 8f, baseY - h),
                new Color(0.30f, 0.27f, 0.24f), 5f);
            DrawLine(new Vector2(sx, baseY - h * 0.55f), new Vector2(sx - 18f, baseY - h * 0.85f),
                new Color(0.30f, 0.27f, 0.24f), 3f);
            DrawLine(new Vector2(sx, baseY - h * 0.7f), new Vector2(sx + 20f, baseY - h),
                new Color(0.30f, 0.27f, 0.24f), 3f);
        }
    }

    /// <summary>稳定伪随机（同一棵树每帧长一样）</summary>
    private static float HashOff(int i)
    {
        float s = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
        return (s - Mathf.Floor(s)) * 2f - 1f; // -1..1
    }

    private void DrawSigns()
    {
        // 充电点：一面小旗 + 充电桩
        if (cfg?.ChargeKms != null)
        {
            for (int i = chargeIdx; i < cfg.ChargeKms.Count; i++)
            {
                float sx = ScreenXFor(cfg.ChargeKms[i]);
                if (sx < -120f || sx > Size.X + 120f)
                    continue;
                DrawChargeStop(sx);
            }
        }
    }

    private void DrawChargeStop(float sx)
    {
        // 桩
        DrawRect(new Rect2(sx - 14f, RoadTop - 88f, 28f, 88f), new Color(0.36f, 0.42f, 0.40f));
        DrawRect(new Rect2(sx - 8f, RoadTop - 76f, 16f, 22f), new Color(0.75f, 0.85f, 0.60f));
        // 旗
        DrawLine(new Vector2(sx + 20f, RoadTop + 10f), new Vector2(sx + 20f, RoadTop - 120f), new Color(0.4f, 0.36f, 0.3f), 4f);
        var flag = new Vector2[] { new(sx + 22f, RoadTop - 118f), new(sx + 86f, RoadTop - 104f), new(sx + 22f, RoadTop - 90f) };
        DrawColoredPolygon(flag, new Color(0.85f, 0.55f, 0.35f, 0.95f));
        DrawString(ChoicePanel.GetChoiceFont(), new Vector2(sx - 12f, RoadTop - 46f), "充电",
            HorizontalAlignment.Left, 60f, 18, new Color(0.92f, 0.94f, 0.86f));
    }

    private void DrawBike()
    {
        float bob = Mathf.Sin(kmDone * 9f) * 2.5f;
        float y = RoadMidY + bob;
        var wheelCol = new Color(0.16f, 0.16f, 0.17f);
        var frameCol = new Color(0.20f, 0.20f, 0.22f);
        var bodyCol = new Color(0.13f, 0.12f, 0.13f);

        // 车轮
        DrawArc(new Vector2(BikeX - 34f, y), 17f, 0, Mathf.Tau, 24, wheelCol, 4f);
        DrawArc(new Vector2(BikeX + 30f, y), 17f, 0, Mathf.Tau, 24, wheelCol, 4f);
        // 踏板车车身
        DrawLine(new Vector2(BikeX - 34f, y), new Vector2(BikeX - 6f, y - 16f), frameCol, 5f);
        DrawLine(new Vector2(BikeX - 6f, y - 16f), new Vector2(BikeX + 24f, y - 14f), frameCol, 5f);
        DrawLine(new Vector2(BikeX + 24f, y - 14f), new Vector2(BikeX + 30f, y), frameCol, 5f);
        DrawLine(new Vector2(BikeX + 24f, y - 14f), new Vector2(BikeX + 30f, y - 40f), frameCol, 4f); // 把立
        DrawLine(new Vector2(BikeX + 20f, y - 42f), new Vector2(BikeX + 42f, y - 40f), frameCol, 4f); // 车把

        // 人：加速前倾、滑行直立、推车弯腰
        float lean = pushing ? -0.15f : (holdAccel && !IsDialogueBusy()) ? 0.28f : 0.05f;
        float hipX = BikeX - 4f, hipY = y - 34f;
        float shoulderX = hipX + 26f * lean + 8f, shoulderY = y - 62f + (pushing ? 8f : 0f);
        DrawLine(new Vector2(hipX, hipY), new Vector2(shoulderX, shoulderY), bodyCol, 7f);          // 躯干
        DrawLine(new Vector2(hipX, hipY), new Vector2(hipX - 14f, y - 4f), bodyCol, 6f);            // 腿
        DrawLine(new Vector2(shoulderX, shoulderY), new Vector2(BikeX + 30f, y - 42f), bodyCol, 5f); // 手臂
        DrawCircle(new Vector2(shoulderX + 4f, shoulderY - 14f), 9f, bodyCol);                       // 头
        // 围巾（第九章她给的挂在颈后，跑起来飘）
        var scarf = new System.Collections.Generic.List<Vector2>
        {
            new(shoulderX - 2f, shoulderY + 4f),
            new(shoulderX - 18f - lean * 10f, shoulderY + 12f + Mathf.Sin(kmDone * 11f) * 3f),
            new(shoulderX - 34f - lean * 16f, shoulderY + 8f + Mathf.Sin(kmDone * 11f + 1f) * 5f),
        };
        DrawPolyline(scarf.ToArray(), new Color(0.72f, 0.36f, 0.36f), 4f);
    }

    private void DrawRain(Vector2 vp)
    {
        if (rainNow < 0.05f)
            return;
        int count = (int)(140 * rainNow);
        var col = new Color(0.78f, 0.84f, 0.90f, 0.38f);
        for (int i = 0; i < count; i++)
        {
            // 哈希布雨：稳定随机 + 快速下落循环
            float hx = HashOff(i * 3 + 1) * 0.5f + 0.5f;
            float speed = 900f + (HashOff(i * 7 + 2) * 0.5f + 0.5f) * 500f;
            float drift = 190f * rainNow;
            float cycle = (vp.Y + 60f);
            float y = Mathf.PosMod(HashOff(i * 13 + 3) * 10000f - rideTime * speed, cycle) - 30f;
            float x = Mathf.PosMod(hx * vp.X - rideTime * drift, vp.X + 40f) - 20f;
            DrawLine(new Vector2(x, y), new Vector2(x - drift * 0.02f, y + 17f), col, 2f);
        }
    }
}
