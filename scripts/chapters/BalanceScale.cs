using Godot;

/// <summary>
/// 天平 —— 第三章《天平》的核心道具（纯代码绘制，无美术素材）
///
/// 一根横梁架在立柱上，两端各吊一个托盘：
///   左盘：堆"记忆纸片"（每放一张回忆，多一片）
///   右盘：落着一颗很小的、很新的光点
///
/// 横梁角度用指数平滑逼近目标值（SetTilt），待机时轻轻摇摆——
/// 像一件很旧、却还在认真工作的东西。
/// </summary>
public partial class BalanceScale : Node2D
{
    // ---------- 形状常量（本地下标：原点 = 横梁转轴） ----------
    private const float BeamHalf = 330f;      // 横梁半长
    private const float BeamThickness = 13f;
    private const float StringLen = 140f;     // 吊绳长
    private const float PanWidth = 214f;
    private const float PanHeight = 24f;
    private const float GroundY = 170f;       // 底座落地高度
    private const float ColumnWidth = 15f;

    // ---------- 配色 ----------
    private static readonly Color MetalDark = new(0.32f, 0.26f, 0.21f);
    private static readonly Color MetalMid = new(0.56f, 0.45f, 0.35f);
    private static readonly Color MetalLight = new(0.75f, 0.63f, 0.48f);
    private static readonly Color ChipPaper = new(0.96f, 0.91f, 0.81f);
    private static readonly Color ChipPaper2 = new(0.90f, 0.84f, 0.72f);
    private static readonly Color ChipEdge = new(0.72f, 0.62f, 0.47f);
    // "补偿物"的纸：冷灰蓝，和暖白的回忆一眼能分开
    private static readonly Color CompensationPaper = new(0.86f, 0.87f, 0.90f);
    private static readonly Color CompensationPaper2 = new(0.79f, 0.81f, 0.86f);
    private static readonly Color CompensationEdge = new(0.55f, 0.58f, 0.66f);
    private static readonly Color OrbWarm = new(1.0f, 0.55f, 0.5f);

    private float targetAngle;   // 目标倾角（度；正 = 右端下沉）
    private float currentAngle;  // 当前角（平滑逼近）
    private float timeAcc;
    private const float IdleAmp = 1.0f; // 待机轻摇幅度（度）

    /// <summary>左托盘上的记忆纸片数量（三张卡片）</summary>
    public int MemoryChips { get; private set; }

    /// <summary>左托盘上的"补偿物"数量（道歉 / 成绩 / 熬夜——越加越歪，天平的失败互动）</summary>
    public int CompensationChips { get; private set; }

    /// <summary>右托盘的小光点是否可见</summary>
    public bool OrbVisible { get; set; } = true;

    private static readonly float[] ChipTilt = { -0.06f, 0.05f, -0.08f, 0.09f, -0.03f, 0.07f, -0.1f, 0.04f, 0.06f, -0.05f };
    private static readonly float[] ChipJitter = { -6f, 7f, -3f, 9f, 0f, -8f, 4f, -2f, 6f, -9f };

    /// <summary>设置目标倾角（度；正 = 右端下沉）。角度一变，木头就"吱呀"一声。</summary>
    public void SetTilt(float degrees)
    {
        if (Mathf.IsEqualApprox(degrees, targetAngle))
            return;
        targetAngle = degrees;
        AudioManager.Instance?.PlaySfx(AudioManager.SfxScaleCreak, -16f, 0.08f);
    }

    /// <summary>往左托盘加一张记忆纸片</summary>
    public void AddMemoryChip() => MemoryChips++;

    /// <summary>往左托盘加一样"补偿物"（第十一轮 C7：想让它平衡，却越加越歪）</summary>
    public void AddCompensationChip() => CompensationChips++;

    // ---------- 坐标换算 ----------

    /// <summary>托盘位置（本地坐标）：按给定角度算吊点，再垂下来</summary>
    private static Vector2 PanLocal(bool left, float angleDeg)
    {
        float a = Mathf.DegToRad(angleDeg);
        float dir = left ? -1f : 1f;
        var end = new Vector2(dir * Mathf.Cos(a), dir * Mathf.Sin(a)) * BeamHalf;
        return end + new Vector2(0, StringLen);
    }

    /// <summary>左盘上方（卡片飞过来的落点，本地坐标，按目标角度算）</summary>
    public Vector2 LeftPanDropLocal() => PanLocal(true, targetAngle) + new Vector2(0, -30f);

    /// <summary>左盘落点的全局坐标（给 Control 层的 Tween 用）</summary>
    public Vector2 LeftPanDropGlobal() => GetGlobalTransform() * LeftPanDropLocal();

    public override void _Process(double delta)
    {
        timeAcc += (float)delta;
        // 指数趋近目标角：任何帧率下都稳定，转向过程中自然经过"平衡点"
        currentAngle = Mathf.Lerp(currentAngle, targetAngle, 1f - Mathf.Exp(-2.6f * (float)delta));
        QueueRedraw();
    }

    public override void _Draw()
    {
        float shown = currentAngle + IdleAmp * Mathf.Sin(timeAcc * 1.15f);
        float a = Mathf.DegToRad(shown);

        Vector2 rightEnd = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * BeamHalf;
        Vector2 leftEnd = -rightEnd;
        Vector2 leftPan = leftEnd + new Vector2(0, StringLen);
        Vector2 rightPan = rightEnd + new Vector2(0, StringLen);

        // 1) 底座 + 立柱（不随横梁动）
        DrawColoredPolygon(new Vector2[]
        {
            new(-150, GroundY + 34), new(150, GroundY + 34),
            new(96, GroundY), new(-96, GroundY),
        }, MetalDark);
        DrawRect(new Rect2(-ColumnWidth / 2f, 0, ColumnWidth, GroundY), MetalMid);
        DrawRect(new Rect2(-ColumnWidth / 2f, 0, 4, GroundY), MetalLight);
        DrawCircle(Vector2.Zero, 22, MetalDark);

        // 2) 吊绳 + 托盘
        DrawPanWithStrings(leftEnd, leftPan);
        DrawPanWithStrings(rightEnd, rightPan);

        // 3) 左盘的纸片堆：先是三张回忆（暖白纸），再是"补偿物"（冷灰纸）
        //    每一张都比上一张歪得更厉害，像随手码上去、越码越不稳
        int total = MemoryChips + CompensationChips;
        for (int i = 0; i < total && i < ChipTilt.Length; i++)
        {
            bool isCompensation = i >= MemoryChips;
            float cx = leftPan.X + ChipJitter[i] + i * 2f;
            float cy = leftPan.Y - 12f - i * 15f;
            DrawSetTransform(new Vector2(cx, cy), ChipTilt[i], Vector2.One);
            Color face = isCompensation
                ? (i % 2 == 0 ? CompensationPaper : CompensationPaper2)
                : (i % 2 == 0 ? ChipPaper : ChipPaper2);
            DrawRect(new Rect2(-62, -7, 124, 14), face);
            DrawRect(new Rect2(-62, -7, 124, 14), isCompensation ? CompensationEdge : ChipEdge, false, 2f);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // 4) 右盘的小光点（会呼吸）
        if (OrbVisible)
        {
            float pulse = 0.8f + 0.2f * Mathf.Sin(timeAcc * 2.1f);
            Vector2 o = rightPan + new Vector2(0, -16);
            DrawCircle(o, 34 * pulse, new Color(OrbWarm, 0.06f));
            DrawCircle(o, 22 * pulse, new Color(OrbWarm, 0.13f));
            DrawCircle(o, 12, new Color(OrbWarm, 0.5f));
            DrawCircle(o, 5.5f, new Color(1f, 0.88f, 0.8f, 0.95f));
        }

        // 5) 横梁（旋转绘制，盖在吊绳结上面）
        DrawSetTransform(Vector2.Zero, a, Vector2.One);
        DrawLine(new Vector2(-BeamHalf, 0), new Vector2(BeamHalf, 0), MetalDark, BeamThickness + 4, true);
        DrawLine(new Vector2(-BeamHalf, 0), new Vector2(BeamHalf, 0), MetalMid, BeamThickness, true);
        DrawCircle(new Vector2(-BeamHalf, 0), 10, MetalLight);
        DrawCircle(new Vector2(BeamHalf, 0), 10, MetalLight);
        DrawCircle(Vector2.Zero, 15, MetalLight);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>画一个托盘：两根八字吊绳 + 浅浅的梯形盘面</summary>
    private void DrawPanWithStrings(Vector2 end, Vector2 pan)
    {
        float spread = PanWidth * 0.38f;
        DrawLine(end, pan + new Vector2(-spread, 0), MetalDark, 3, true);
        DrawLine(end, pan + new Vector2(spread, 0), MetalDark, 3, true);

        float half = PanWidth / 2f;
        DrawColoredPolygon(new Vector2[]
        {
            new(pan.X - half, pan.Y),
            new(pan.X + half, pan.Y),
            new(pan.X + half - 22, pan.Y + PanHeight),
            new(pan.X - half + 22, pan.Y + PanHeight),
        }, MetalMid);
        DrawLine(new Vector2(pan.X - half, pan.Y), new Vector2(pan.X + half, pan.Y), MetalLight, 4, true);
    }
}
