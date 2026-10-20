using Godot;

/// <summary>
/// 第十五章《那束光》的网咖场景——**程序绘制**（2026-10-20）。
///
/// 【为什么不用图】生图服务从 2026-10-18 起一直 TLS 握手失败（
/// "socket disconnected before secure TLS connection was established"），
/// 连试多次不通。这一章又不能拿宿舍/街道的图凑（网咖的蓝光是全章母题：
/// 她睡着之后，那束光正好打在她眼睛上——玩家做的第一件事就是把它挡掉）。
/// 所以按第三章天平、第十二章骑行的老办法：用 _Draw 直接画。
///
/// 【两种模式】
///   Hall  —— 大厅：三排机器 + 天花板灯管 + 前台的玻璃柜 + 霓虹灯牌
///   Booth —— 包间：两台并排的屏幕 + 桌面纸杯烟盒 + 两把旧沙发椅
///
/// 【两个可调参数】
///   Dim        0~1  深夜压光（02:10 之后整体变暗、灯管只留一盏）
///   HerScreen  0~1  她那台屏幕的亮度（挡光那一拍会把它压到 0.1）
/// </summary>
public partial class NetcafeArt : Control
{
    public enum ViewMode { Hall, Booth }

    private ViewMode mode = ViewMode.Booth;
    private float dim;
    private float herScreen = 1f;

    /// <summary>大厅 / 包间</summary>
    public ViewMode Mode
    {
        get => mode;
        set { mode = value; QueueRedraw(); }
    }

    /// <summary>深夜压光：0 = 刚进门，1 = 凌晨两点半</summary>
    public float Dim
    {
        get => dim;
        set { dim = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
    }

    /// <summary>她那台机器的屏幕亮度（1 = 正常，0.1 = 只剩一层灰）</summary>
    public float HerScreen
    {
        get => herScreen;
        set { herScreen = Mathf.Clamp(value, 0f, 1f); QueueRedraw(); }
    }

    // ==================== 配色（淡极生艳：低饱和底 + 一处高饱和蓝） ====================

    private static readonly Color WallTop = new("161a22");
    private static readonly Color WallBottom = new("0e1116");
    private static readonly Color DeskCol = new("1b1f27");
    private static readonly Color DeskEdge = new("2c323d");
    private static readonly Color ChairCol = new("12151b");
    private static readonly Color ScreenCore = new("8fd3f4");
    private static readonly Color ScreenMid = new("3f8fbf");
    private static readonly Color ScreenDeep = new("1d4f70");
    private static readonly Color NeonCol = new("6fb6e8");
    private static readonly Color LampCol = new("f0c98a");

    public override void _Draw()
    {
        var s = Size;
        if (s.X < 8 || s.Y < 8)
            return;

        // 背景：两层实色叠出上下渐变（不用 shader，导出稳）
        DrawRect(new Rect2(Vector2.Zero, s), WallTop);
        int bands = 12;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)bands;
            var c = WallTop.Lerp(WallBottom, t);
            DrawRect(new Rect2(0, s.Y * t, s.X, s.Y / bands + 1), c);
        }

        if (mode == ViewMode.Hall)
            DrawHall(s);
        else
            DrawBooth(s);

        // 深夜：整体压一层蓝黑
        if (dim > 0.01f)
            DrawRect(new Rect2(Vector2.Zero, s), new Color(0.02f, 0.03f, 0.06f, 0.55f * dim));
    }

    // ==================== 大厅 ====================

    private void DrawHall(Vector2 s)
    {
        // 天花板灯管：两根，深夜只留右边一根
        float lampA = 1f - dim * 0.85f;
        DrawGlowRect(new Rect2(s.X * 0.12f, s.Y * 0.06f, s.X * 0.30f, s.Y * 0.018f),
                     LampCol, 0.55f * lampA, 26f);
        if (dim < 0.5f)
            DrawGlowRect(new Rect2(s.X * 0.55f, s.Y * 0.06f, s.X * 0.30f, s.Y * 0.018f),
                         LampCol, 0.45f * lampA, 26f);

        // 靠墙三排机器（远景，只画屏）
        float rowStartY = s.Y * 0.30f;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                float x = s.X * (0.06f + col * 0.115f);
                float y = rowStartY + row * s.Y * 0.14f;
                var r = new Rect2(x, y, s.X * 0.075f, s.Y * 0.062f);
                float k = ScreenBrightness(row, col);
                DrawScreen(r, k * (1f - dim * 0.55f), glowScale: 0.8f);
            }
        }

        // 前台：左下的台子 + 玻璃柜
        var desk = new Rect2(0, s.Y * 0.66f, s.X * 0.34f, s.Y * 0.34f);
        DrawRect(desk, DeskCol);
        DrawRect(new Rect2(desk.Position, new Vector2(desk.Size.X, s.Y * 0.006f)), DeskEdge);
        // 柜子里的泡面/饮料：几块小色块，不必认得出
        for (int i = 0; i < 4; i++)
        {
            var cup = new Rect2(s.X * (0.03f + i * 0.055f), s.Y * 0.71f, s.X * 0.032f, s.Y * 0.035f);
            DrawRect(cup, i % 2 == 0 ? new Color("b9622f") : new Color("2f7a4f"));
        }
        // 柜台的冷白灯
        DrawGlowRect(new Rect2(s.X * 0.02f, s.Y * 0.665f, s.X * 0.30f, s.Y * 0.010f),
                     new Color("d8e6f0"), 0.35f, 18f);

        // 霓虹灯牌（右上）：一个描边矩形 + 两条虚光
        var sign = new Rect2(s.X * 0.60f, s.Y * 0.12f, s.X * 0.22f, s.Y * 0.14f);
        for (int i = 0; i < 3; i++)
        {
            var rr = sign.Grow(-i * 2f);
            DrawRect(rr, new Color(NeonCol, 0.35f - i * 0.10f), filled: false, width: 1.6f);
        }
        DrawGlowRect(new Rect2(sign.Position + new Vector2(0, sign.Size.Y + 4), new Vector2(sign.Size.X, 3)),
                     NeonCol, 0.30f, 20f);

        // 地面反光
        DrawRect(new Rect2(0, s.Y * 0.93f, s.X, s.Y * 0.07f), new Color("0b0e13"));
        DrawRect(new Rect2(s.X * 0.36f, s.Y * 0.94f, s.X * 0.30f, s.Y * 0.003f), new Color(NeonCol, 0.10f));
    }

    // ==================== 包间 ====================

    private void DrawBooth(Vector2 s)
    {
        // 隔音棉：墙上一格一格的深色方块
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                var q = new Rect2(s.X * (0.03f + col * 0.108f), s.Y * (0.08f + row * 0.105f),
                                  s.X * 0.090f, s.Y * 0.085f);
                DrawRect(q, new Color(1, 1, 1, 0.018f));
            }
        }

        // 天花板那盏暖黄小灯：深夜会先灭掉（Dim 越大越暗）
        float lampK = 1f - dim * 0.9f;
        DrawGlowRect(new Rect2(s.X * 0.455f, s.Y * 0.028f, s.X * 0.09f, s.Y * 0.016f),
                     LampCol, 0.55f * lampK, 40f);
        DrawRect(new Rect2(s.X * 0.478f, 0f, s.X * 0.004f, s.Y * 0.028f),
                 new Color(0.35f, 0.33f, 0.30f, 0.8f));
        if (lampK > 0.2f)
            DrawCircle(new Vector2(s.X * 0.5f, s.Y * 0.30f), s.X * 0.34f,
                       new Color(LampCol, 0.020f * lampK));

        // 屏幕在墙上的溢光（两台各一团，撑起整个房间的蓝色）
        DrawRect(new Rect2(s.X * 0.02f, s.Y * 0.13f, s.X * 0.46f, s.Y * 0.50f),
                 new Color(ScreenMid, 0.045f));
        DrawRect(new Rect2(s.X * 0.52f, s.Y * 0.13f, s.X * 0.46f, s.Y * 0.50f),
                 new Color(ScreenMid, 0.045f * herScreen));

        // 桌面
        var deskTop = s.Y * 0.615f;
        DrawRect(new Rect2(0, deskTop, s.X, s.Y * 0.20f), DeskCol);
        DrawRect(new Rect2(0, deskTop, s.X, s.Y * 0.008f), DeskEdge);
        DrawRect(new Rect2(0, deskTop + s.Y * 0.20f, s.X, s.Y * 0.012f), new Color("090b0f"));

        // 桌面上屏幕的倒影：两道很淡的竖条
        DrawRect(new Rect2(s.X * 0.16f, deskTop + 4, s.X * 0.22f, s.Y * 0.10f),
                 new Color(ScreenCore, 0.035f));
        DrawRect(new Rect2(s.X * 0.66f, deskTop + 4, s.X * 0.22f, s.Y * 0.10f),
                 new Color(ScreenCore, 0.035f * herScreen));

        // 两台显示器
        DrawMonitor(new Rect2(s.X * 0.075f, s.Y * 0.205f, s.X * 0.355f, s.Y * 0.335f), 1f);
        DrawMonitor(new Rect2(s.X * 0.570f, s.Y * 0.205f, s.X * 0.355f, s.Y * 0.335f), herScreen);

        // 桌面小物：两个纸杯 + 烟盒
        DrawCup(new Vector2(s.X * 0.300f, deskTop + s.Y * 0.045f), s.X * 0.026f);
        DrawCup(new Vector2(s.X * 0.345f, deskTop + s.Y * 0.050f), s.X * 0.024f);
        // 烟盒：一整个晚上没拆的那种
        var pack = new Rect2(s.X * 0.505f, deskTop + s.Y * 0.072f, s.X * 0.058f, s.Y * 0.030f);
        DrawRect(pack, new Color("ddd6c8"));
        DrawRect(new Rect2(pack.Position + new Vector2(0, pack.Size.Y * 0.22f),
                           new Vector2(pack.Size.X, pack.Size.Y * 0.16f)), new Color("2d4a63"));
        DrawRect(new Rect2(pack.Position + new Vector2(pack.Size.X * 0.12f, pack.Size.Y * 0.52f),
                           new Vector2(pack.Size.X * 0.62f, 1.5f)), new Color("8a8378"));
        DrawRect(new Rect2(pack.Position + new Vector2(pack.Size.X * 0.12f, pack.Size.Y * 0.66f),
                           new Vector2(pack.Size.X * 0.44f, 1.5f)), new Color("8a8378"));

        // 两把旧沙发椅（下缘剪影）
        DrawChair(new Rect2(s.X * 0.045f, s.Y * 0.86f, s.X * 0.36f, s.Y * 0.30f));
        DrawChair(new Rect2(s.X * 0.595f, s.Y * 0.86f, s.X * 0.36f, s.Y * 0.30f));

        // 她那台屏幕打到她脸上的那束光（这一章的母题）
        DrawHerLightBeam(s);
    }

    /// <summary>显示器：外框 + 屏幕（三层色带模拟渐变）+ 溢出的光晕 + 支架</summary>
    private void DrawMonitor(Rect2 r, float brightness)
    {
        // 光晕先画，免得盖住屏幕
        DrawGlowRect(r.Grow(8f), ScreenMid, 0.10f * brightness, 46f);
        // 外框
        DrawRect(r, new Color("0a0c10"));
        var inner = r.Grow(-6f);
        DrawScreen(inner, brightness * (1f - dim * 0.25f));
        // 支架
        DrawRect(new Rect2(r.Position.X + r.Size.X * 0.42f, r.End.Y, r.Size.X * 0.16f, 16f),
                 new Color("0d1015"));
        DrawRect(new Rect2(r.Position.X + r.Size.X * 0.30f, r.End.Y + 14f, r.Size.X * 0.40f, 6f),
                 new Color("0d1015"));
    }

    /// <summary>屏幕本体：近黑底 + 一块柔和的冷光。柔光用径向渐变贴图，
    /// 不用"叠几个圆"——那样会露出同心圆环，像音频可视化</summary>
    private void DrawScreen(Rect2 r, float k, float glowScale = 1f)
    {
        DrawRect(r, new Color("0a1219"));
        DrawRect(r, new Color(ScreenMid, 0.06f * k));   // 整体一层很淡的蓝

        // 中心偏下的柔光
        var halo = new Rect2(r.Position.X - r.Size.X * 0.10f,
                             r.Position.Y + r.Size.Y * 0.06f,
                             r.Size.X * 1.20f, r.Size.Y * 1.10f);
        DrawTextureRect(GlowTex(), halo, false, new Color(ScreenCore, 0.30f * k));

        // 内容：两三块很淡的"界面"
        DrawRect(new Rect2(r.Position + new Vector2(r.Size.X * 0.10f, r.Size.Y * 0.16f),
                           r.Size * new Vector2(0.34f, 0.09f)), new Color(1, 1, 1, 0.045f * k));
        DrawRect(new Rect2(r.Position + new Vector2(r.Size.X * 0.56f, r.Size.Y * 0.34f),
                           r.Size * new Vector2(0.30f, 0.13f)), new Color(ScreenCore, 0.035f * k));
        // 底部一条 UI 条
        DrawRect(new Rect2(r.Position + new Vector2(0, r.Size.Y * 0.82f),
                           new Vector2(r.Size.X, r.Size.Y * 0.04f)), new Color(ScreenCore, 0.07f * k));

        // 边缘压暗：假装是曲面屏的边
        DrawRect(r, new Color(0, 0, 0, 0.40f), filled: false, width: Mathf.Max(3f, r.Size.X * 0.05f));

        if (k > 0.05f)
            DrawGlowRect(r, ScreenCore, 0.05f * k * glowScale, 40f);
    }

    // ==================== 柔光贴图（只建一次） ====================

    private static GradientTexture2D glowTex;

    private static GradientTexture2D GlowTex()
    {
        if (glowTex != null)
            return glowTex;

        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 1));
        g.SetColor(1, new Color(1, 1, 1, 0));
        glowTex = new GradientTexture2D
        {
            Gradient = g,
            Width = 128,
            Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };
        return glowTex;
    }

    /// <summary>她那台屏幕朝左前方打出去的一束光——她睡着时正打在她脸上</summary>
    private void DrawHerLightBeam(Vector2 s)
    {
        float k = herScreen * (1f - dim * 0.3f);
        if (k < 0.05f)
            return;

        var from = new Vector2(s.X * 0.70f, s.Y * 0.36f);
        var pts = new Vector2[]
        {
            from,
            new Vector2(s.X * 0.34f, s.Y * 0.30f),
            new Vector2(s.X * 0.30f, s.Y * 0.72f),
            new Vector2(s.X * 0.70f, s.Y * 0.52f),
        };
        DrawColoredPolygon(pts, new Color(ScreenCore, 0.055f * k));
        var inner = new Vector2[]
        {
            from,
            new Vector2(s.X * 0.40f, s.Y * 0.34f),
            new Vector2(s.X * 0.37f, s.Y * 0.64f),
            new Vector2(s.X * 0.70f, s.Y * 0.50f),
        };
        DrawColoredPolygon(inner, new Color(ScreenCore, 0.05f * k));
    }

    private void DrawChair(Rect2 r)
    {
        DrawRect(r, ChairCol);
        // 靠背的一点高光，勾出皮的磨痕
        DrawRect(new Rect2(r.Position + new Vector2(r.Size.X * 0.08f, r.Size.Y * 0.10f),
                           new Vector2(r.Size.X * 0.84f, 2f)), new Color(1, 1, 1, 0.045f));
    }

    private void DrawCup(Vector2 center, float w)
    {
        float h = w * 1.35f;
        var pts = new Vector2[]
        {
            new(center.X - w * 0.42f, center.Y - h * 0.5f),
            new(center.X + w * 0.42f, center.Y - h * 0.5f),
            new(center.X + w * 0.30f, center.Y + h * 0.5f),
            new(center.X - w * 0.30f, center.Y + h * 0.5f),
        };
        DrawColoredPolygon(pts, new Color("cfc6b4"));
        DrawRect(new Rect2(center.X - w * 0.42f, center.Y - h * 0.5f, w * 0.84f, 2f),
                 new Color("a89e8c"));
    }

    /// <summary>带柔边的光块：从外到内叠几层低透明度矩形</summary>
    private void DrawGlowRect(Rect2 r, Color c, float alpha, float spread)
    {
        for (int i = 3; i >= 1; i--)
        {
            var g = r.Grow(spread * i / 3f);
            DrawRect(g, new Color(c, alpha / (i + 1)));
        }
    }

    /// <summary>大厅里那些机器的亮度：给一点随机感，别像贴图</summary>
    private float ScreenBrightness(int row, int col)
    {
        int h = (row * 37 + col * 61) % 11;
        return 0.45f + h / 22f;
    }
}
