using Godot;
using System;

/// <summary>
/// 第七章《三点水》——台球桌小演出（纯代码绘制）
///
/// 【摆在哪】
/// 场景里是一个 Node2D，原点正好落在背景图里那颗静止白球的球心上
/// （luomandike_v1.png 的 (1093,650)px → 1920x1080 屏幕坐标 (1172,688)）。
/// 下面所有点位都是"相对白球球心"的局部坐标，单位屏幕像素。
///
/// 【坐标标定】（拿网格覆盖原图一格一格量出来的）
///   球盘右端球   → 局部 (-296,  73)   （我这一杆，蓝球滚出的起点）
///   球盘左端球   → 局部 (-485,  67)   （她这一杆，红球滚出的起点）
///   右角袋口     → 局部 ( 216,  66)
///   左角袋口     → 局部 (-855,  84)
///
/// 【演出】
///   Strike()：球杆送白球出去（滑向左边的球盘方向）→ 撞响（clack）
///             → 蓝球从球盘右端滚出来，贴着下库边拐一个大弯进右角袋（pocket_drop）。
///   Pocket()：红球从球盘左端滚出来，沿着远库溜进左角袋。
///   白球离开原位的时候，下面垫一块"台呢补丁"盖住背景里那颗静止的白球，
///   回来时补丁自动收掉。
///
/// 【为什么这么画】
/// 背景是一张静态插画：白球和球盘里的球都"焊"在图上。
/// 所以演出的思路是——白球我们重新画一颗盖在原来那颗上面（可以动），
/// 彩球用"从球盘边缘淡入"的方式出场（看起来像刚从球堆里滑出来），
/// 进袋时缩小变淡、沉进袋口。代价最小的"活起来"。
/// </summary>
public partial class BilliardShot : Node2D
{
    // ==================== 几何尺寸 ====================
    private const float BallR = 17.5f;       // 白球半径（屏幕像素）
    private const float ColoredR = 22f;      // 彩球半径（球盘在近景，球更大一点）

    // ==================== 关键点位（局部坐标，白球球心为原点） ====================
    private static readonly Vector2 WhiteHome = Vector2.Zero;
    private static readonly Vector2 WhiteSlideEnd = new(-260f, -10f);  // 白球送到这里再弹回来
    private static readonly Vector2 TrayRightStart = new(-296f, 73f);
    private static readonly Vector2 TrayLeftStart = new(-485f, 67f);
    private static readonly Vector2 RightPocket = new(216f, 66f);
    private static readonly Vector2 LeftPocket = new(-855f, 84f);

    // ==================== 颜色（从背景图上采样） ====================
    private static readonly Color FeltBase = new(173 / 255f, 191 / 255f, 118 / 255f); // 台呢主色
    private static readonly Color FeltLit = new(195 / 255f, 205 / 255f, 118 / 255f);  // 亮处
    private static readonly Color FeltDim = new(154 / 255f, 179 / 255f, 118 / 255f);  // 暗处
    private static readonly Color WhiteBallCol = new(228 / 255f, 221 / 255f, 192 / 255f);
    private static readonly Color BallHighlight = new(250 / 255f, 248 / 255f, 238 / 255f);
    private static readonly Color BlueBallCol = new(62 / 255f, 104 / 255f, 188 / 255f);
    private static readonly Color RedBallCol = new(198 / 255f, 64 / 255f, 62 / 255f);
    private static readonly Color StickWood = new(0.76f, 0.59f, 0.38f);
    private static readonly Color StickTipCol = new(0.93f, 0.91f, 0.86f);
    private static readonly Color StickCollar = new(0.29f, 0.23f, 0.18f);

    // ==================== 时序（秒） ====================
    private const float SlideDur = 0.30f;    // 白球送出去
    private const float ReturnDur = 0.32f;   // 白球弹回来
    private const float RollDur = 1.15f;     // 彩球滚动
    private const float FadeInDur = 0.15f;   // 彩球淡入
    private const float DropDur = 0.28f;     // 落袋（缩小变淡）

    // ==================== 状态 ====================
    private enum Mode { Idle, Strike, Pocket }
    private Mode mode = Mode.Idle;
    private float t;                 // 当前动作计时
    private Action pendingDone;
    private bool clackFired;
    private bool dropSoundFired;

    private Vector2 whitePos = WhiteHome;   // 白球当前位置
    private float stickAlpha;               // 球杆透明度

    private Vector2 ballP0, ballCtrl, ballP2; // 彩球贝塞尔轨迹
    private Vector2 ballPos;
    private float ballAlpha;
    private float ballScale = 1f;
    private Color ballColor = BlueBallCol;  // 默认我那一杆：蓝球

    // ==================== 对外 API ====================

    /// <summary>我的一杆：白球送出去 → 蓝球从球盘右端滚进右角袋</summary>
    public void Strike(Action onDone)
    {
        if (mode != Mode.Idle)
            return;
        mode = Mode.Strike;
        t = 0f;
        clackFired = false;
        dropSoundFired = false;
        pendingDone = onDone;
        ballColor = BlueBallCol;
        ballP0 = TrayRightStart;
        ballP2 = RightPocket;
        // 控制点：从球盘右端出发，贴着下库边兜一个大弯，再拐进角袋
        ballCtrl = new Vector2((ballP0.X + ballP2.X) * 0.5f, Mathf.Max(ballP0.Y, ballP2.Y) + 124f);
        ballPos = ballP0;
        ballAlpha = 0f;
        ballScale = 1f;
        QueueRedraw();
    }

    /// <summary>她的一杆：红球从球盘左端滚出来，沿远库溜进左角袋</summary>
    public void Pocket(Action onDone)
    {
        if (mode != Mode.Idle)
            return;
        mode = Mode.Pocket;
        t = 0f;
        dropSoundFired = false;
        pendingDone = onDone;
        ballColor = RedBallCol;
        ballP0 = TrayLeftStart;
        ballP2 = LeftPocket;
        // 控制点往上鼓一点：贴着远侧的库边溜过去
        ballCtrl = new Vector2((ballP0.X + ballP2.X) * 0.5f, Mathf.Min(ballP0.Y, ballP2.Y) - 96f);
        ballPos = ballP0;
        ballAlpha = 0f;
        ballScale = 1f;
        QueueRedraw();
    }

    // ==================== 动画 ====================

    public override void _Process(double delta)
    {
        if (mode == Mode.Idle)
            return;

        t += (float)delta;

        if (mode == Mode.Strike)
            StepStrike();
        else
            StepPocket();

        QueueRedraw();

        if (mode != Mode.Idle)
            return;

        var cb = pendingDone;
        pendingDone = null;
        cb?.Invoke();
    }

    private void StepStrike()
    {
        // 1) 送杆：白球加速滑出去，球杆跟着
        if (t < SlideDur)
        {
            float k = EaseInQuad(t / SlideDur);
            whitePos = WhiteHome.Lerp(WhiteSlideEnd, k);
            stickAlpha = 1f;
            return;
        }

        // 2) 撞响（只响一次）
        if (!clackFired)
        {
            clackFired = true;
            whitePos = WhiteSlideEnd;
            AudioManager.Instance?.PlaySfx(AudioManager.SfxBallClack, -7f, 0.05f);
        }

        float after = t - SlideDur;

        // 3) 白球弹回原位（球杆随手收掉）
        whitePos = WhiteSlideEnd.Lerp(WhiteHome, EaseOutQuad(Mathf.Min(1f, after / ReturnDur)));
        stickAlpha = Mathf.Max(0f, 1f - after / 0.14f);

        // 4) 蓝球淡入 → 滚向角袋 → 落袋
        UpdateRollingBall(after, RollDur);

        // 5) 全部演完
        if (after >= FadeInDur + RollDur + DropDur)
        {
            mode = Mode.Idle;
            whitePos = WhiteHome;
            stickAlpha = 0f;
        }
    }

    private void StepPocket()
    {
        UpdateRollingBall(t, RollDur);

        if (t >= FadeInDur + RollDur + DropDur)
            mode = Mode.Idle;
    }

    /// <summary>彩球：先淡入，再沿贝塞尔滚过去，最后缩小变淡沉进袋口</summary>
    private void UpdateRollingBall(float after, float rollTotal)
    {
        if (after < FadeInDur)
        {
            ballPos = ballP0;
            ballAlpha = after / FadeInDur;
            return;
        }

        float rollT = after - FadeInDur;
        if (rollT < rollTotal)
        {
            ballAlpha = 1f;
            float k = EaseInOutQuad(Mathf.Min(1f, rollT / rollTotal));
            ballPos = Bezier2(ballP0, ballCtrl, ballP2, k);
            return;
        }

        float dropT = rollT - rollTotal;
        if (dropT < DropDur)
        {
            PlayDropOnce(); // 入袋声紧跟落袋动画（只响一次）
            ballPos = ballP2;
            float k = Mathf.Min(1f, dropT / DropDur);
            ballScale = Mathf.Lerp(1f, 0.35f, EaseInQuad(k));
            ballAlpha = 1f - EaseInQuad(k);
            return;
        }

        ballAlpha = 0f;
        ballScale = 0.35f;
    }

    private void PlayDropOnce()
    {
        if (dropSoundFired)
            return;
        dropSoundFired = true;
        AudioManager.Instance?.PlaySfx(AudioManager.SfxPocketDrop, -10f, 0.05f);
    }

    // ==================== 绘制 ====================

    public override void _Draw()
    {
        // 白球离开原位时：先垫台呢补丁，盖住背景里的静止白球
        float away = whitePos.DistanceTo(WhiteHome);
        if (away > 2f)
            DrawFeltPatch(WhiteHome, Mathf.Clamp(away / 12f, 0f, 1f));

        // 滚动的彩球
        if (ballAlpha > 0.01f)
            DrawRackBall(ballPos, ballScale, ballAlpha, ballColor);

        // 白球（自绘，盖在背景那颗上面）
        DrawWhiteBall();

        // 球杆（只在送杆阶段露头）
        if (stickAlpha > 0.01f)
            DrawStick();
    }

    private void DrawWhiteBall()
    {
        // 移动时才画影子（停在原位时背景已经有烘焙好的影子，别画两层）
        float away = whitePos.DistanceTo(WhiteHome);
        if (away > 2f)
            DrawEllipse(whitePos + new Vector2(3f, 8f), BallR * 1.05f, BallR * 0.5f,
                new Color(0.16f, 0.2f, 0.1f, 0.18f));

        DrawCircle(whitePos, BallR, WhiteBallCol);
        DrawCircle(whitePos + new Vector2(-6f, -7f), BallR * 0.42f, BallHighlight with { A = 0.85f });
        DrawArc(whitePos, BallR, 0f, Mathf.Tau, 32, new Color(0.58f, 0.56f, 0.48f, 0.5f), 1.4f, true);
    }

    private void DrawRackBall(Vector2 pos, float scale, float alpha, Color col)
    {
        float r = ColoredR * scale;
        if (r < 1f)
            return;

        var baseCol = col with { A = alpha };
        var hiCol = col.Lerp(Colors.White, 0.55f) with { A = alpha * 0.8f };
        var darkCol = col.Darkened(0.45f) with { A = alpha * 0.7f };

        DrawCircle(pos + new Vector2(2f, 7f) * scale, r * 1.0f, new Color(0.16f, 0.2f, 0.1f, 0.2f * alpha));
        DrawCircle(pos, r, baseCol);
        DrawCircle(pos + new Vector2(-6f, -7f) * scale, r * 0.4f, hiCol);
        DrawArc(pos, r, 0f, Mathf.Tau, 32, darkCol, 1.6f * scale, true);
    }

    private void DrawStick()
    {
        var dir = WhiteSlideEnd.Normalized();          // 送杆方向（往左偏一点）
        var back = whitePos - dir * (BallR + 4f);      // 皮头贴住球面
        var tipTail = back - dir * 12f;
        var collTail = back - dir * 22f;
        var shaftTail = back - dir * 178f;

        DrawLine(collTail, shaftTail, StickWood with { A = stickAlpha }, 9f, true);
        DrawLine(tipTail, collTail, StickCollar with { A = stickAlpha }, 9.5f, true);
        DrawLine(back, tipTail, StickTipCol with { A = stickAlpha }, 9f, true);
    }

    /// <summary>台呢补丁：几层椭圆叠出羽化边缘（亮处/暗处各偏一点，贴合背景光影）</summary>
    private void DrawFeltPatch(Vector2 at, float alpha)
    {
        DrawEllipse(at, 34f, 36f, FeltBase with { A = 0.12f * alpha });
        DrawEllipse(at, 27f, 29f, FeltBase with { A = 0.38f * alpha });
        DrawEllipse(at, 22f, 24f, FeltBase with { A = alpha });
        DrawEllipse(at + new Vector2(-5f, -7f), 13f, 11f, FeltLit with { A = 0.5f * alpha });
        DrawEllipse(at + new Vector2(5f, 7f), 15f, 13f, FeltDim with { A = 0.5f * alpha });
    }

    /// <summary>画一个椭圆（Godot 没有现成的 DrawEllipse，用多边形凑）</summary>
    private void DrawEllipse(Vector2 center, float rx, float ry, Color col)
    {
        const int Segments = 26;
        var pts = new Vector2[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float a = Mathf.Tau * i / Segments;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, col);
    }

    // ==================== 小工具 ====================

    private static float EaseInQuad(float x) => x * x;

    private static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);

    private static float EaseInOutQuad(float x) =>
        x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;

    private static Vector2 Bezier2(Vector2 p0, Vector2 c, Vector2 p1, float k)
    {
        float u = 1f - k;
        return u * u * p0 + 2f * u * k * c + k * k * p1;
    }
}
