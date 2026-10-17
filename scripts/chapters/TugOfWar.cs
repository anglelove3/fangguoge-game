using Godot;

/// <summary>
/// 第五章《星月》——「拉扯」演出：抓住她的手腕（第三轮·分手章核心交互）
///
/// 她说要走。屏幕上出现她的手（剪影），你按住 = 抓住。
/// 她往前挣，手一点点往右滑；你的鼠标得追着它，追得上就抓得牢，
/// 追不上就滑脱。也可以随时松开——松手的人比抓住的人先学会告别。
///
/// 两条路同一个结局，演出不同：
///   · 主动松开 → 手停在半空，慢慢垂下
///   · 滑脱   → 手从指尖滑出去，什么都不剩
/// </summary>
public partial class TugOfWar : Control
{
    private readonly System.Action<bool> onResolved; // true=主动松开 / false=滑脱
    private float t;
    private bool resolved;

    private Vector2 handPos;       // 她的手的当前位置
    private Vector2 handVel;
    private bool holding;          // 鼠标按着
    private float grip;            // 抓握强度 0~1（靠近手=1，远了衰减）

    private const float Dur = 7.5f;         // 总时长上限（超时强制滑脱）
    private const float CatchRadius = 90f;  // 抓握判定半径

    public TugOfWar(System.Action<bool> resolved)
    {
        onResolved = resolved;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var size = GetViewportRect().Size;
        handPos = new Vector2(size.X * 0.42f, size.Y * 0.52f);
    }

    public override void _Input(InputEvent @event)
    {
        if (resolved)
            return;
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
                holding = true;
            else if (holding)
                Resolve(true); // 主动松开
        }
        else if (@event is InputEventMouseMotion)
        {
            // 鼠标移动时更新抓握
        }
    }

    public override void _Process(double delta)
    {
        if (resolved)
            return;
        float dt = (float)delta;
        t += dt;

        var mouse = GetGlobalMousePosition();
        float dist = handPos.DistanceTo(mouse);
        grip = holding ? Mathf.Clamp(1f - dist / CatchRadius, 0f, 1f) : 0f;

        // 她在挣：手缓慢向右滑（速度随时间加快——越到后面越拉不住）
        float pull = 26f + t * 7f;             // 挣脱速度
        float holdBack = grip * 0.62f;         // 抓住时的减速
        handVel = new Vector2(pull * (1f - holdBack), Mathf.Sin(t * 2.2f) * 6f);
        handPos += handVel * dt;

        // 追丢了：手滑出屏幕右缘 = 滑脱
        if (handPos.X > GetViewportRect().Size.X * 0.96f || t >= Dur)
        {
            Resolve(false);
            return;
        }

        QueueRedraw();
    }

    private void Resolve(bool releasedByPlayer)
    {
        if (resolved)
            return;
        resolved = true;
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -16f);
        // 短暂停留，让「松开/滑脱」的瞬间被看见
        var tree = GetTree();
        if (tree != null)
        {
            var timer = tree.CreateTimer(1.4);
            timer.Timeout += () =>
            {
                var cb = onResolved;
                QueueFree();
                cb?.Invoke(releasedByPlayer);
            };
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        // 背景微压暗，注意力收到手上
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.02f, 0.02f, 0.05f, 0.45f));

        // 她的手（剪影：纤细的手腕+五指轮廓，深色偏暖）
        DrawHand(handPos, holding ? Mathf.Max(grip, 0.15f) : 0f);

        // 抓着的时候：鼠标和手之间一根绷紧的线（张力可视化）
        if (holding && grip > 0.05f)
        {
            var mouse = GetGlobalMousePosition();
            float tension = 1f - grip; // 越松线越亮越抖
            var jitter = new Vector2(
                (float)GD.RandRange(-3, 3) * tension,
                (float)GD.RandRange(-3, 3) * tension);
            DrawLine(handPos, mouse + jitter,
                new Color(1f, 0.9f, 0.75f, 0.35f + tension * 0.3f), 2f + tension * 2f);
        }

        // 抓得越久画面边缘越紧（暗角收缩）
        if (holding && grip > 0.3f)
        {
            float squeeze = Mathf.Min(t / 5f, 1f) * grip * 0.28f;
            DrawRect(new Rect2(0, 0, size.X, size.Y * squeeze), new Color(0, 0, 0, 0.4f));
            DrawRect(new Rect2(0, size.Y * (1f - squeeze), size.X, size.Y * squeeze), new Color(0, 0, 0, 0.4f));
        }

        // 提示文字（开场两秒）
        if (t < 2.2f)
        {
            var font = GetThemeDefaultFont();
            DrawString(font, new Vector2(size.X / 2f - 130f, size.Y * 0.82f),
                "（按住她，别让她走）", HorizontalAlignment.Left, -1, 24,
                new Color(1f, 0.95f, 0.85f, 0.85f * Mathf.Min(t, 2.2f - t)));
        }
    }

    /// <summary>画一只剪影的手（手腕朝左，指尖朝右——正在离开的方向）</summary>
    private void DrawHand(Vector2 p, float gripGlow)
    {
        var skin = new Color(0.16f, 0.13f, 0.14f, 0.92f);
        // 手掌
        DrawEllipse(p, 26f, 20f, skin);
        // 五指（指尖朝右）
        for (int i = 0; i < 5; i++)
        {
            float fy = p.Y - 14f + i * 7f;
            float len = 20f + (i == 2 ? 6f : i is 1 or 3 ? 3f : 0f); // 中指最长
            DrawLine(new Vector2(p.X + 18f, fy), new Vector2(p.X + 18f + len, fy - 2f), skin, 7f);
        }
        // 手腕
        DrawRect(new Rect2(p.X - 52f, p.Y - 14f, 42f, 28f), skin);

        // 被抓的微微亮边（抓握反馈）
        if (gripGlow > 0.02f)
            DrawArc(p, 32f, 0f, Mathf.Tau, 28, new Color(1f, 0.92f, 0.8f, gripGlow * 0.4f), 2.4f);
    }

    private void DrawEllipse(Vector2 center, float rx, float ry, Color c)
    {
        const int N = 20;
        var pts = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float a = Mathf.Tau * i / N;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, c);
    }
}
