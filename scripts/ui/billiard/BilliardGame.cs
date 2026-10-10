using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 第七章：罗曼蒂克台球桌 —— 一局真能打的台球（第十五轮）
///
/// 【为什么有它】
/// 试玩反馈（第十五轮 #10）："台球要更有参与感，真的可以在游戏里打台球。"
/// 原来那套 BilliardShot 是两杆固定动画（白球滑出去、彩球拐弯进袋），好看但只能看。
/// 现在换成"贴脸视角"的一局真球：自己拖杆（方向 + 力度 + 杆法），
/// 打进彩球得分，江洁会接杆、会点评，三杆定胜负，赢了输了都有彩蛋台词。
///
/// 【玩法】
///   - 按住屏幕往后拉 —— 拉的方向的反面就是出杆方向，拉得越远劲越大；松手出杆。
///   - 右下角"杆法"：点白球上的位置，高杆/低杆/左塞/右塞（点击球心 = 定杆）。
///   - 白球落袋 = 犯规，白球摆回开球点（不扣分）；彩球进袋 +1 分。
///   - 我和江洁各打三杆，比总分：赢 / 输 / 平 各有一组彩蛋台词。
///
/// 【实现口径】
///   - 物理：240Hz 定步长，球-球弹性碰撞（等质量）、库边反弹、摩擦衰减；
///     这是"手感"所在，参数都在下面常量区。
///   - 绘制：全部自绘（台框/台呢/袋口/球/瞄准线/力度条/杆法盘），不依赖新美术。
///   - 台词：全在 data/billiard/billiard.json；结果记进"选择回顾"（ch07_billiard）。
///   - 模态规矩和手机/电脑一样：打开时抑制对话输入，关闭时恢复 + 回调。
/// </summary>
public partial class BilliardGame : Control
{
    // ==================== 球桌几何（1920x1080 设计坐标） ====================

    private static readonly Rect2 FrameRect = new(240, 96, 1440, 800);  // 木框外沿
    private const float Rail = 46f;                                      // 木边宽
    private static readonly Rect2 FeltRect = new(286, 142, 1348, 716);   // 台呢（球心活动区）

    private const float BallR = 15f;        // 球半径
    private const float PocketVisR = 34f;   // 袋口画出来的半径
    private const float PocketCapR = 26f;   // 球心进到这个圈里就算落袋

    // ==================== 物理参数 ====================

    private const float PhysStep = 1f / 240f;   // 定步长
    private const float Friction = 1.05f;       // 摩擦（指数衰减系数，每秒）
    private const float StopV = 7f;             // 低于这个速度就当停稳
    private const float CushionE = 0.72f;       // 库边弹性
    private const float BallE = 0.97f;          // 球球碰撞弹性
    private const float SpeedMin = 340f;        // 最小出杆速度
    private const float SpeedMax = 2000f;       // 满力出杆速度
    private const float MaxDrag = 300f;         // 拉满力度需要的像素
    private const float DeadDrag = 18f;         // 小于这个距离 = 取消，不出杆

    // ==================== 配色 ====================

    private static readonly Color ScrimCol = new(0.03f, 0.05f, 0.04f, 0.68f);
    private static readonly Color WoodCol = new(0.44f, 0.29f, 0.19f);
    private static readonly Color WoodEdge = new(0.24f, 0.15f, 0.09f);
    private static readonly Color FeltCol = new(0.34f, 0.53f, 0.31f);
    private static readonly Color FeltDark = new(0.10f, 0.09f, 0.08f);
    private static readonly Color WhiteBallCol = new(0.93f, 0.91f, 0.84f);
    private static readonly Color InkCol = new(0.14f, 0.12f, 0.10f);
    private static readonly Color AimCol = new(0.98f, 0.96f, 0.82f);
    private static readonly Color AimGhostCol = new(0.98f, 0.96f, 0.82f, 0.75f);
    private static readonly Color HerAimCol = new(0.96f, 0.70f, 0.34f);
    private static readonly Color UIDim = new(0.90f, 0.87f, 0.78f);

    private static readonly Color[] BallCols =
    {
        new(0.90f, 0.72f, 0.20f), // 1 黄
        new(0.25f, 0.45f, 0.82f), // 2 蓝
        new(0.82f, 0.24f, 0.22f), // 3 红
        new(0.55f, 0.30f, 0.75f), // 4 紫
        new(0.90f, 0.52f, 0.18f), // 5 橙
        new(0.16f, 0.62f, 0.58f), // 6 青
    };

    // ==================== 球 ====================

    private class Ball
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public Color Col;
        public int Number;      // 0 = 白球
        public bool Pocketed;
    }

    private class PocketFx
    {
        public Vector2 Pos;
        public Color Col;
        public int Number;
        public float T;         // 0 → 1
    }

    private readonly List<Ball> balls = new();
    private readonly List<PocketFx> pocketFx = new();
    private Vector2[] pockets;

    // ==================== 流程状态 ====================

    private enum GState { Intro, Aim, Sim, Comment, HerThink, Outro }

    private GState gs = GState.Intro;
    private int myShots, herShots, myScore, herScore;
    private bool lastShooterMe;
    private bool turnIsMine = true;   // HUD 显示当前该谁打
    private bool shotScratch;
    private int shotPocketCount;

    private readonly List<BilliardData.CommentLine> commentQueue = new();
    private Action afterComments;
    private bool commentActive;
    private float commentT;

    private readonly Dictionary<string, int> lastPick = new();
    private readonly RandomNumberGenerator rng = new();

    private float herThinkT;
    private Vector2 herAimDir = Vector2.Right;
    private bool herAimShow;

    private float flowT;
    private Action flowAction;

    // ==================== 瞄准/杆法 ====================

    private bool dragging;
    private bool spinDragging;
    private Vector2 dragFrom, dragNow;
    private Vector2 spinOff = Vector2.Zero;     // 单位圆内；x=右塞，y 负=高杆
    private float curFollow, curSide;           // 出杆时结算的杆法（高杆+ / 低杆−）
    private Vector2 curShotDir = Vector2.Right;

    // ==================== 物理积累 ====================

    private float physAcc;

    // ==================== 节点 ====================

    private BoardView board;
    private Label scoreLabel;
    private Label turnLabel;
    private Label hintLabel;
    private Label spinLabel;
    private Control commentBar;
    private Label commentSpeaker;
    private Label commentText;
    private Tween barTween;

    private bool opened;
    private bool closing;
    private bool suppressing;
    private bool aimHintShown;

    public event Action Closed;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        board = new BoardView { G = this, MouseFilter = MouseFilterEnum.Ignore };
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(board);

        scoreLabel = MakeHudLabel(18, 60, 34, new Color(0.99f, 0.97f, 0.90f));
        scoreLabel.Name = "ScoreLabel";
        turnLabel = MakeHudLabel(62, 94, 22, UIDim);
        turnLabel.Name = "TurnLabel";
        hintLabel = MakeHudLabel(892, 924, 20, new Color(0.93f, 0.88f, 0.70f));
        hintLabel.Name = "HintLabel";
        hintLabel.Visible = false;
        spinLabel = MakeHudLabel(0, 0, 20, UIDim);
        spinLabel.Name = "SpinLabel";
        spinLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        spinLabel.Position = new Vector2(1652 - 150, 918);
        spinLabel.Size = new Vector2(300, 28);
        spinLabel.HorizontalAlignment = HorizontalAlignment.Center;

        BuildCommentBar();
        SetupTable();
    }

    private Label MakeHudLabel(float top, float bottom, int fontSize, Color col)
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.SetAnchorsPreset(LayoutPreset.TopWide);
        l.OffsetLeft = 0; l.OffsetRight = 0;
        l.OffsetTop = top; l.OffsetBottom = bottom;
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        AddChild(l);
        return l;
    }

    private void BuildCommentBar()
    {
        commentBar = new Control { Name = "CommentBar", MouseFilter = MouseFilterEnum.Ignore };
        commentBar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        commentBar.Position = new Vector2(520, 946);
        commentBar.Size = new Vector2(880, 96);
        AddChild(commentBar);

        var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.08f, 0.82f),
            BorderColor = new Color(0.90f, 0.80f, 0.55f, 0.35f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        };
        style.SetCornerRadiusAll(16);
        style.ContentMarginLeft = 22;
        style.ContentMarginRight = 22;
        bg.AddThemeStyleboxOverride("panel", style);
        commentBar.AddChild(bg);

        commentSpeaker = new Label { MouseFilter = MouseFilterEnum.Ignore };
        commentSpeaker.Position = new Vector2(24, 10);
        commentSpeaker.AddThemeFontSizeOverride("font_size", 21);
        commentBar.AddChild(commentSpeaker);

        commentText = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        commentText.Position = new Vector2(24, 40);
        commentText.Size = new Vector2(832, 50);
        commentText.AddThemeFontSizeOverride("font_size", 25);
        commentText.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 0.90f));
        commentBar.AddChild(commentText);

        commentBar.Visible = false;
    }

    /// <summary>开局（由章节调用）：摆球、抑制对话输入、淡入、放"开场点评"</summary>
    public void Open()
    {
        if (opened)
            return;
        opened = true;

        // 台面遮罩会盖住角落按钮，把背包/返回菜单抬到遮罩之上，免得发灰看不清
        SetCornerButtonsRaised(true);

        suppressing = true;
        DialogueManager.Instance?.SetUiSuppressed(true);

        Modulate = new Color(1, 1, 1, 0);
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 1f, 0.3).SetEase(Tween.EaseType.Out);

        UpdateHud(true);
        spinLabel.Text = HudText("spin") + "：" + SpinName();

        gs = GState.Comment;
        ShowComments(BilliardData.Comments("start"), BeginPlayerTurn);
    }

    public void Close()
    {
        if (closing)
            return;
        closing = true;

        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, 0.22).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(() =>
        {
            if (suppressing)
            {
                suppressing = false;
                DialogueManager.Instance?.SetUiSuppressed(false);
            }
            SetCornerButtonsRaised(false);
            Closed?.Invoke();
            QueueFree();
        }));
    }

    public override void _ExitTree()
    {
        // 兜底：没走 Close 就被销毁（切场景）时别把背景输入锁死
        if (suppressing && !closing)
        {
            suppressing = false;
            DialogueManager.Instance?.SetUiSuppressed(false);
        }
        SetCornerButtonsRaised(false);
    }

    /// <summary>把左上背包 / 右上返回菜单抬到台面遮罩之上（或还原）</summary>
    private void SetCornerButtonsRaised(bool raised)
    {
        var parent = GetParent();
        if (parent == null)
            return;
        foreach (var n in new[] { "BagButton", "BackButton" })
        {
            var b = parent.GetNodeOrNull<Control>(n);
            if (b != null)
                b.ZIndex = raised ? 100 : 0;
        }
    }

    // ==================== 摆球 ====================

    private void SetupTable()
    {
        float cx = FeltRect.Position.X + FeltRect.Size.X * 0.5f;
        float cy = FeltRect.Position.Y + FeltRect.Size.Y * 0.5f;

        pockets = new[]
        {
            new Vector2(FeltRect.Position.X, FeltRect.Position.Y),
            new Vector2(cx, FeltRect.Position.Y),
            new Vector2(FeltRect.End.X, FeltRect.Position.Y),
            new Vector2(FeltRect.Position.X, FeltRect.End.Y),
            new Vector2(cx, FeltRect.End.Y),
            new Vector2(FeltRect.End.X, FeltRect.End.Y),
        };

        balls.Clear();
        balls.Add(new Ball { Pos = new Vector2(FeltRect.Position.X + FeltRect.Size.X * 0.24f, cy), Col = WhiteBallCol, Number = 0 });

        // 三排三角（1 / 2·3 / 4·5·6），顶球朝白球
        float apexX = FeltRect.Position.X + FeltRect.Size.X * 0.70f;
        float rowDx = 2f * BallR * 0.866f;
        int number = 1;
        for (int row = 0; row < 3; row++)
        {
            for (int i = 0; i <= row; i++)
            {
                balls.Add(new Ball
                {
                    Pos = new Vector2(apexX + row * (rowDx + 0.5f), cy + (i * 2f - row) * BallR),
                    Col = BallCols[(number - 1) % BallCols.Length],
                    Number = number,
                });
                number++;
            }
        }
    }

    // ==================== 每帧 ====================

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 物理推进（定步长 + 防死循环上限）
        if (gs == GState.Sim)
        {
            physAcc += dt;
            int guard = 0;
            while (physAcc >= PhysStep && guard < 16)
            {
                StepOnce(PhysStep);
                physAcc -= PhysStep;
                guard++;
            }
            if (guard >= 16)
                physAcc = 0;

            if (AllStopped())
                ResolveShot();
        }

        // 落袋小特效
        for (int i = pocketFx.Count - 1; i >= 0; i--)
        {
            pocketFx[i].T += dt * 3.0f;
            if (pocketFx[i].T >= 1f)
                pocketFx.RemoveAt(i);
        }

        // 点评条计时
        if (commentActive)
        {
            commentT -= dt;
            if (commentT <= 0f)
                ShowNextComment();
        }

        // 一次性流程计时（她看线 / 提示自动收起）
        if (flowAction != null)
        {
            flowT -= dt;
            if (flowT <= 0f)
            {
                var a = flowAction;
                flowAction = null;
                a();
            }
        }

        board.QueueRedraw();
    }

    // ==================== 物理 ====================

    private void StepOnce(float dt)
    {
        float minX = FeltRect.Position.X + BallR;
        float maxX = FeltRect.End.X - BallR;
        float minY = FeltRect.Position.Y + BallR;
        float maxY = FeltRect.End.Y - BallR;

        foreach (var b in balls)
        {
            if (b.Pocketed)
                continue;

            b.Pos += b.Vel * dt;

            if (b.Vel.LengthSquared() > 1f)
            {
                b.Vel *= Mathf.Exp(-Friction * dt);
                if (b.Vel.Length() < StopV)
                    b.Vel = Vector2.Zero;
            }

            // 落袋（先判袋口：贴库滑到袋口也算进）
            foreach (var p in pockets)
            {
                if ((b.Pos - p).Length() < PocketCapR)
                {
                    OnBallPocketed(b, p);
                    break;
                }
            }
            if (b.Pocketed)
                continue;

            // 库边反弹
            if (b.Pos.X < minX) { b.Pos.X = minX; b.Vel.X = Mathf.Abs(b.Vel.X) * CushionE; }
            if (b.Pos.X > maxX) { b.Pos.X = maxX; b.Vel.X = -Mathf.Abs(b.Vel.X) * CushionE; }
            if (b.Pos.Y < minY) { b.Pos.Y = minY; b.Vel.Y = Mathf.Abs(b.Vel.Y) * CushionE; }
            if (b.Pos.Y > maxY) { b.Pos.Y = maxY; b.Vel.Y = -Mathf.Abs(b.Vel.Y) * CushionE; }
        }

        // 球球碰撞（7 颗球，两两一遍足够）
        for (int i = 0; i < balls.Count; i++)
        {
            var a = balls[i];
            if (a.Pocketed)
                continue;
            for (int j = i + 1; j < balls.Count; j++)
            {
                var b = balls[j];
                if (b.Pocketed)
                    continue;

                Vector2 d = b.Pos - a.Pos;
                float dist = d.Length();
                if (dist >= 2f * BallR || dist < 0.0001f)
                    continue;

                Vector2 n = d / dist;
                float overlap = 2f * BallR - dist;
                a.Pos -= n * (overlap * 0.5f);
                b.Pos += n * (overlap * 0.5f);

                float velN = (a.Vel - b.Vel).Dot(n);
                if (velN <= 0f)
                    continue; // 已经在分开，别硬塞能量

                float imp = velN * (1f + BallE) * 0.5f;
                a.Vel -= n * imp;
                b.Vel += n * imp;

                // 杆法：白球碰到别的球的那一下，把高/低杆、左右塞"卸"出来
                if (a.Number == 0 || b.Number == 0)
                {
                    var white = a.Number == 0 ? a : b;
                    if (Mathf.Abs(curFollow) > 0.01f || Mathf.Abs(curSide) > 0.01f)
                    {
                        white.Vel += curShotDir * (curFollow * 150f);
                        white.Vel += new Vector2(-n.Y, n.X) * (curSide * 110f);
                        curFollow *= 0.55f;
                        curSide *= 0.55f;
                    }
                }

                if (velN > 90f)
                {
                    float db = Mathf.Clamp(-9f - velN / 320f, -24f, -9f);
                    AudioManager.Instance?.PlaySfx(AudioManager.SfxBallClack, db, 0.04f);
                }
            }
        }
    }

    private void OnBallPocketed(Ball b, Vector2 pocket)
    {
        b.Pocketed = true;
        b.Vel = Vector2.Zero;
        pocketFx.Add(new PocketFx { Pos = pocket, Col = b.Col, Number = b.Number, T = 0f });
        AudioManager.Instance?.PlaySfx(AudioManager.SfxPocketDrop, -8f, 0.05f);

        if (b.Number == 0)
            shotScratch = true;
        else
            shotPocketCount++;
    }

    private bool AllStopped()
    {
        foreach (var b in balls)
        {
            if (!b.Pocketed && b.Vel != Vector2.Zero)
                return false;
        }
        return true;
    }

    // ==================== 出杆 / 回合 ====================

    private void Fire(Vector2 dir, float power, float follow, float side, bool isMe)
    {
        var white = balls[0];
        curShotDir = dir.Normalized();
        curFollow = follow;
        curSide = side;
        white.Vel = curShotDir * Mathf.Lerp(SpeedMin, SpeedMax, power);
        white.Pocketed = false;

        lastShooterMe = isMe;
        if (isMe)
            myShots++;
        else
            herShots++;

        shotScratch = false;
        shotPocketCount = 0;
        physAcc = 0f;
        gs = GState.Sim;

        AudioManager.Instance?.PlaySfx(AudioManager.SfxBallClack, -6f, 0.03f);
        UpdateHud();
    }

    private void ResolveShot()
    {
        if (lastShooterMe)
            myScore += shotPocketCount;
        else
            herScore += shotPocketCount;

        if (shotScratch)
            RespotWhite();

        UpdateHud();

        string key = lastShooterMe
            ? (shotScratch ? "playerScratch" : (shotPocketCount > 0 ? "playerPocket" : "playerMiss"))
            : (shotScratch ? "herScratch" : (shotPocketCount > 0 ? "herPocket" : "herMiss"));

        var line = PickComment(key);
        gs = GState.Comment;
        if (line != null)
            ShowComments(new List<BilliardData.CommentLine> { line }, NextPhase);
        else
            NextPhase();
    }

    private void RespotWhite()
    {
        var white = balls[0];
        white.Pocketed = false;
        white.Vel = Vector2.Zero;
        white.Pos = new Vector2(FeltRect.Position.X + FeltRect.Size.X * 0.24f, FeltRect.Position.Y + FeltRect.Size.Y * 0.5f);

        int guard = 0;
        while (BallAt(white.Pos, white) && guard < 20)
        {
            white.Pos += new Vector2(2.4f * BallR, 0f);
            guard++;
        }
    }

    private bool BallAt(Vector2 pos, Ball self)
    {
        foreach (var b in balls)
        {
            if (b == self || b.Pocketed)
                continue;
            if ((b.Pos - pos).Length() < 2f * BallR + 1f)
                return true;
        }
        return false;
    }

    private void NextPhase()
    {
        // 彩球打光了就提前收官
        bool anyColored = false;
        foreach (var b in balls)
        {
            if (b.Number > 0 && !b.Pocketed)
            {
                anyColored = true;
                break;
            }
        }

        if (!anyColored || (myShots >= 3 && herShots >= 3))
        {
            EndGame();
            return;
        }

        if (lastShooterMe)
            BeginHerTurn();
        else
            BeginPlayerTurn();
    }

    private void BeginPlayerTurn()
    {
        gs = GState.Aim;
        turnIsMine = true;
        dragging = false;
        spinDragging = false;
        if (!aimHintShown)
        {
            aimHintShown = true;
            hintLabel.Text = HudText("aimHint");
            hintLabel.Visible = true;
            flowT = 4.5f;
            flowAction = () => { hintLabel.Visible = false; };
        }
        UpdateHud();
    }

    private void BeginHerTurn()
    {
        gs = GState.Comment;
        turnIsMine = false;
        ShowComments(BilliardData.Comments("herTurn"), BeginHerThink);
    }

    private void BeginHerThink()
    {
        gs = GState.HerThink;
        PlanHerShot(out Vector2 dir, out float power);
        herAimDir = dir;
        herAimShow = true;
        hintLabel.Text = HudText("sheThinks");
        hintLabel.Visible = true;

        flowT = 0.9f;
        flowAction = () =>
        {
            herAimShow = false;
            hintLabel.Visible = false;
            Fire(herAimDir, power, 0f, 0f, false);
        };
        UpdateHud();
    }

    private void EndGame()
    {
        int result = myScore > herScore ? 0 : (myScore < herScore ? 1 : 2);
        GameManager.Instance?.RecordChoice("ch07_billiard", result);

        string key = result == 0 ? "win" : (result == 1 ? "lose" : "tie");
        var line = PickComment(key);

        gs = GState.Comment;
        ShowComments(line != null
            ? new List<BilliardData.CommentLine> { line }
            : new List<BilliardData.CommentLine>(), () =>
        {
            gs = GState.Outro;
            hintLabel.Text = HudText("clickContinue");
            hintLabel.Visible = true;
        });
    }

    // ==================== 江洁的 AI ====================

    /// <summary>挑一颗目标球 + 一个袋口，对着"幽灵球"位打；带一点点手感误差</summary>
    private void PlanHerShot(out Vector2 dir, out float power)
    {
        var white = balls[0];
        Ball best = null;
        Vector2 bestDir = Vector2.Right;
        float bestScore = float.MaxValue;
        float bestDist = 600f;

        foreach (var b in balls)
        {
            if (b.Number == 0 || b.Pocketed)
                continue;

            foreach (var p in pockets)
            {
                Vector2 dirBP = (p - b.Pos).Normalized();
                Vector2 ghost = b.Pos - dirBP * (2f * BallR);

                float dW = (ghost - white.Pos).Length();
                if (dW < 70f)
                    continue; // 贴脸球没角度，换一颗

                float dBP = (b.Pos - p).Length();
                float score = dW + dBP * 0.9f;
                if (PathBlocked(white.Pos, ghost, white, b))
                    score += 900f;
                if (PathBlocked(b.Pos, p, b, null))
                    score += 700f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = b;
                    bestDir = (ghost - white.Pos).Normalized();
                    bestDist = dW + dBP;
                }
            }
        }

        if (best == null)
        {
            // 没得选：对着最近的一颗轻轻碰一下
            float nearest = float.MaxValue;
            foreach (var b in balls)
            {
                if (b.Number == 0 || b.Pocketed)
                    continue;
                float d = (b.Pos - white.Pos).Length();
                if (d < nearest)
                {
                    nearest = d;
                    bestDir = (b.Pos - white.Pos).Normalized();
                    bestDist = d;
                }
            }
        }

        // 手感误差：转个 ±0.42°（她手稳，这样中近距离能进、长台会歪）
        // 为什么是 0.42 而不是更大：袋口 26px、球到袋 500px 时允许的方向误差只有 ~0.13°，
        // 第一版给 ±1.2° 的结果是她三杆打空（r15 回归第一轮终局 0:0），"她确实稳"的人设就塌了
        float jitter = Mathf.DegToRad(rng.RandfRange(-0.42f, 0.42f));
        dir = bestDir.Rotated(jitter);

        float p0 = Mathf.Clamp(0.42f + bestDist * 0.00028f, 0.5f, 0.92f);
        p0 *= 1f + rng.RandfRange(-0.05f, 0.05f);
        power = Mathf.Clamp(p0, 0.45f, 0.95f);
    }

    private bool PathBlocked(Vector2 a, Vector2 b, Ball ignoreA, Ball ignoreB)
    {
        for (int i = 1; i <= 11; i++)
        {
            Vector2 p = a.Lerp(b, i / 12f);
            foreach (var ball in balls)
            {
                if (ball == ignoreA || ball == ignoreB || ball.Pocketed)
                    continue;
                if ((p - ball.Pos).Length() < 2f * BallR - 2f)
                    return true;
            }
        }
        return false;
    }

    // ==================== 台词条 ====================

    private BilliardData.CommentLine PickComment(string key)
    {
        var list = BilliardData.Comments(key);
        if (list.Count == 0)
            return null;
        int idx = rng.RandiRange(0, list.Count - 1);
        if (list.Count > 1 && lastPick.TryGetValue(key, out int last) && idx == last)
            idx = (idx + 1) % list.Count;
        lastPick[key] = idx;
        return list[idx];
    }

    private void ShowComments(List<BilliardData.CommentLine> list, Action after)
    {
        commentQueue.Clear();
        commentQueue.AddRange(list);
        afterComments = after;
        commentActive = true;
        ShowNextComment();
    }

    private void ShowNextComment()
    {
        if (commentQueue.Count == 0)
        {
            commentActive = false;
            HideCommentBar();
            var a = afterComments;
            afterComments = null;
            a?.Invoke();
            return;
        }

        var line = commentQueue[0];
        commentQueue.RemoveAt(0);

        commentSpeaker.Text = line.Speaker;
        commentSpeaker.AddThemeColorOverride("font_color", SpeakerColor(line.Speaker));
        commentText.Text = line.Text;
        commentT = line.Hold;

        commentBar.Visible = true;
        barTween?.Kill();
        commentBar.Modulate = new Color(1, 1, 1, 0);
        barTween = CreateTween();
        barTween.TweenProperty(commentBar, "modulate:a", 1f, 0.16);
    }

    private void HideCommentBar()
    {
        barTween?.Kill();
        barTween = CreateTween();
        barTween.TweenProperty(commentBar, "modulate:a", 0f, 0.14);
        barTween.TweenCallback(Callable.From(() => commentBar.Visible = false));
    }

    private void SkipComment()
    {
        if (!commentActive)
            return;
        ShowNextComment();
    }

    private static Color SpeakerColor(string s)
    {
        if (s.Contains("江洁")) return new Color(0.98f, 0.62f, 0.70f);
        if (s.Contains("俊杰")) return new Color(0.60f, 0.78f, 0.98f);
        if (s.Contains("金艮")) return new Color(0.90f, 0.80f, 0.58f);
        if (s.Contains("小米")) return new Color(0.98f, 0.78f, 0.52f);
        return new Color(0.72f, 0.92f, 0.74f);
    }

    // ==================== HUD ====================

    private string HudText(string key)
    {
        var h = BilliardData.Data.Hud;
        return key switch
        {
            "scoreMe" => h.ScoreMe,
            "scoreHer" => h.ScoreHer,
            "yourTurn" => h.YourTurn,
            "herTurn" => h.HerTurn,
            "shotN" => h.ShotN,
            "power" => h.Power,
            "spin" => h.Spin,
            "aimHint" => h.AimHint,
            "sheThinks" => h.SheThinks,
            "clickContinue" => h.ClickContinue,
            _ => key,
        };
    }

    private void UpdateHud(bool initial = false)
    {
        _ = initial;
        scoreLabel.Text = HudText("scoreMe") + "  " + myScore + " : " + herScore + "  " + HudText("scoreHer");
        int n = turnIsMine ? Mathf.Min(myShots + 1, 3) : Mathf.Min(herShots + 1, 3);
        turnLabel.Text = string.Format(HudText("shotN"), n) + " · " + (turnIsMine ? HudText("yourTurn") : HudText("herTurn"));
    }

    private void UpdateSpinLabel()
    {
        spinLabel.Text = HudText("spin") + "：" + SpinName();
    }

    private string SpinName()
    {
        var h = BilliardData.Data.Hud;
        if (spinOff.Length() < 0.18f)
            return h.SpinCenter;
        if (Mathf.Abs(spinOff.Y) >= Mathf.Abs(spinOff.X))
            return spinOff.Y < 0 ? h.SpinHigh : h.SpinLow;
        return spinOff.X < 0 ? h.SpinLeft : h.SpinRight;
    }

    // ==================== 输入 ====================

    private static readonly Rect2 SpinZone = new(1596, 928, 112, 112);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
                OnPress(mb.Position);
            else
                OnRelease(mb.Position);
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm)
        {
            if (dragging || spinDragging)
                dragNow = mm.Position;
            if (spinDragging)
            {
                UpdateSpinFromPos(mm.Position);
                board.QueueRedraw();
            }
            if (dragging)
                board.QueueRedraw();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (dragging || spinDragging)
            {
                dragging = false;
                spinDragging = false;
            }
            else if (commentActive)
            {
                SkipComment();
            }
        }
    }

    private void OnPress(Vector2 pos)
    {
        if (commentActive)
        {
            SkipComment();
            return;
        }

        switch (gs)
        {
            case GState.Aim:
                if (SpinZone.HasPoint(pos))
                {
                    spinDragging = true;
                    UpdateSpinFromPos(pos);
                    UpdateSpinLabel();
                }
                else
                {
                    dragging = true;
                    dragFrom = pos;
                    dragNow = pos;
                }
                board.QueueRedraw();
                break;

            case GState.Outro:
                Close();
                break;
        }
    }

    private void OnRelease(Vector2 pos)
    {
        if (spinDragging)
        {
            spinDragging = false;
            return;
        }

        if (!dragging)
            return;
        dragging = false;

        Vector2 pull = dragFrom - pos; // 往后拉的反面就是出杆方向
        if (pull.Length() < DeadDrag)
        {
            board.QueueRedraw();
            return;
        }

        float power = Mathf.Clamp(pull.Length() / MaxDrag, 0f, 1f);
        float follow = -spinOff.Y;
        float side = spinOff.X;
        Fire(pull, power, follow, side, true);
    }

    private void UpdateSpinFromPos(Vector2 pos)
    {
        Vector2 c = new(1652, 984);
        Vector2 off = (pos - c) / 26f; // 26px = 满偏
        if (off.Length() > 1f)
            off = off.Normalized();
        spinOff = off;
        UpdateSpinLabel();
    }

    // ==================== 绘制 ====================

    private partial class BoardView : Control
    {
        public BilliardGame G;

        public override void _Draw()
        {
            G.DrawBoard(this);
        }
    }

    private Font drawFont;

    private void DrawBoard(CanvasItem c)
    {
        drawFont ??= GetThemeDefaultFont();

        // 压暗背景
        c.DrawRect(new Rect2(0, 0, 1920, 1080), ScrimCol);

        // 台框（带一点投影）
        c.DrawRect(new Rect2(FrameRect.Position + new Vector2(6, 12), FrameRect.Size), new Color(0, 0, 0, 0.35f));
        c.DrawRect(FrameRect, WoodCol);
        c.DrawRect(FrameRect, WoodEdge, false, 6f);

        // 台呢
        c.DrawRect(FeltRect, FeltCol);
        c.DrawRect(FeltRect.Grow(-6), new Color(1, 1, 1, 0.045f));
        c.DrawRect(FeltRect, new Color(0.16f, 0.24f, 0.15f, 0.85f), false, 4f);

        // 开球线与置球点
        float headX = FeltRect.Position.X + FeltRect.Size.X * 0.24f;
        c.DrawLine(new Vector2(headX, FeltRect.Position.Y + 6), new Vector2(headX, FeltRect.End.Y - 6),
            new Color(0.62f, 0.76f, 0.55f, 0.38f), 2f);
        float footX = FeltRect.Position.X + FeltRect.Size.X * 0.70f;
        c.DrawCircle(new Vector2(footX - 0.5f * BallR * 1.732f, FeltRect.Position.Y + FeltRect.Size.Y * 0.5f), 3.5f,
            new Color(0.62f, 0.76f, 0.55f, 0.5f));

        // 袋口
        foreach (var p in pockets)
        {
            c.DrawCircle(p, PocketVisR, new Color(0.06f, 0.06f, 0.05f, 0.9f));
            c.DrawCircle(p, PocketVisR - 6, new Color(0.015f, 0.015f, 0.015f, 1f));
        }

        // 落袋小特效（缩小沉进袋口）
        foreach (var fx in pocketFx)
        {
            float k = Mathf.Clamp(fx.T, 0f, 1f);
            float r = BallR * Mathf.Lerp(1f, 0.3f, k);
            float alpha = 1f - k;
            DrawBallAt(c, fx.Pos, r, fx.Col, fx.Number, alpha);
        }

        // 球
        foreach (var b in balls)
        {
            if (b.Pocketed)
                continue;
            DrawBallAt(c, b.Pos, BallR, b.Col, b.Number, 1f);
        }

        // 她的看线
        if (herAimShow && gs == GState.HerThink)
        {
            var white = balls[0];
            DrawAimLine(c, white.Pos, herAimDir, HerAimCol, 0.75f);
        }

        // 我的瞄准（拖杆时）
        if (dragging && gs == GState.Aim)
        {
            Vector2 pull = dragFrom - dragNow;
            if (pull.Length() > DeadDrag)
            {
                DrawAimLine(c, balls[0].Pos, pull.Normalized(), AimCol, 1f);
                DrawPullBack(c);
            }
        }

        DrawPowerBar(c);
        DrawSpinWidget(c);
    }

    private void DrawAimLine(CanvasItem c, Vector2 from, Vector2 dir, Color col, float alpha)
    {
        float tEnd = PredictRay(from, dir, out Ball hitBall, out Vector2 hitPoint);
        Vector2 end = from + dir * tEnd;

        // 虚线
        float total = tEnd;
        float dash = 16f, gap = 10f;
        float t = 26f; // 从球边外一点开始画
        while (t < total)
        {
            float segEnd = Mathf.Min(t + dash, total);
            c.DrawLine(from + dir * t, from + dir * segEnd, col with { A = alpha * 0.9f }, 3f);
            t = segEnd + gap;
        }

        // 幽灵球（白球心到达接触点时的位置）
        if (hitBall != null)
            c.DrawArc(hitPoint, BallR, 0f, Mathf.Tau, 40, AimGhostCol with { A = alpha * 0.75f }, 2f, true);

        // 箭头尖
        var tip = from + dir * Mathf.Max(tEnd - 6f, 30f);
        var left = tip - dir.Rotated(0.42f) * 16f;
        var right = tip - dir.Rotated(-0.42f) * 16f;
        c.DrawColoredPolygon(new[] { tip, left, right }, col with { A = alpha * 0.95f });
    }

    /// <summary>射线第一个撞点：t 是白球心能走的距离；hitBall=撞到的球（null=撞库）</summary>
    private float PredictRay(Vector2 from, Vector2 dir, out Ball hitBall, out Vector2 hitPoint)
    {
        float bestT = float.MaxValue;
        hitBall = null;

        foreach (var b in balls)
        {
            if (b.Number == 0 || b.Pocketed)
                continue;
            Vector2 oc = b.Pos - from;
            float proj = oc.Dot(dir);
            if (proj <= 0)
                continue;
            float perp2 = oc.LengthSquared() - proj * proj;
            float rr = (2f * BallR) * (2f * BallR);
            if (perp2 >= rr)
                continue;
            float t = proj - Mathf.Sqrt(rr - perp2);
            if (t > 0 && t < bestT)
            {
                bestT = t;
                hitBall = b;
            }
        }

        // 库边
        float minX = FeltRect.Position.X + BallR;
        float maxX = FeltRect.End.X - BallR;
        float minY = FeltRect.Position.Y + BallR;
        float maxY = FeltRect.End.Y - BallR;
        if (dir.X > 0.0001f) bestT = Mathf.Min(bestT, (maxX - from.X) / dir.X);
        if (dir.X < -0.0001f) bestT = Mathf.Min(bestT, (minX - from.X) / dir.X);
        if (dir.Y > 0.0001f) bestT = Mathf.Min(bestT, (maxY - from.Y) / dir.Y);
        if (dir.Y < -0.0001f) bestT = Mathf.Min(bestT, (minY - from.Y) / dir.Y);

        hitPoint = from + dir * bestT;
        return bestT;
    }

    /// <summary>拖杆时从白球后面"抽杆"的视觉：一条渐隐的杆影 + 力度方向</summary>
    private void DrawPullBack(CanvasItem c)
    {
        Vector2 pull = dragFrom - dragNow;
        float k = Mathf.Clamp(pull.Length() / MaxDrag, 0f, 1f);
        Vector2 dir = pull.Normalized();
        var white = balls[0];

        var back = white.Pos - dir * (BallR + 6f + k * 34f);
        var tail = back - dir * (120f + k * 40f);
        c.DrawLine(tail, back, new Color(0.76f, 0.59f, 0.38f, 0.85f), 8f, true);
        c.DrawLine(back, back + dir * 14f, new Color(0.93f, 0.91f, 0.86f, 0.9f), 8.4f, true);
    }

    private void DrawBallAt(CanvasItem c, Vector2 pos, float r, Color col, int number, float alpha)
    {
        if (r < 1f)
            return;

        // 影子
        DrawEllipse(c, pos + new Vector2(2.5f, 4.5f), r * 1.02f, r * 0.55f, new Color(0, 0, 0, 0.25f * alpha));
        // 球体
        c.DrawCircle(pos, r, col with { A = alpha });
        // 高光
        c.DrawCircle(pos + new Vector2(-r * 0.34f, -r * 0.4f), r * 0.32f, new Color(1, 1, 1, 0.72f * alpha));
        // 描边
        c.DrawArc(pos, r, 0f, Mathf.Tau, 32, col.Darkened(0.45f) with { A = 0.75f * alpha }, 1.6f, true);

        // 号码
        if (number > 0 && drawFont != null)
        {
            c.DrawCircle(pos, r * 0.52f, new Color(0.96f, 0.95f, 0.90f, alpha));
            string txt = number.ToString();
            var sz = drawFont.GetStringSize(txt, HorizontalAlignment.Left, -1, 15);
            c.DrawString(drawFont, pos + new Vector2(-sz.X * 0.5f, sz.Y * 0.32f), txt,
                HorizontalAlignment.Left, -1, 15, InkCol with { A = alpha });
        }
    }

    private void DrawEllipse(CanvasItem c, Vector2 center, float rx, float ry, Color col)
    {
        const int Seg = 22;
        var pts = new Vector2[Seg];
        for (int i = 0; i < Seg; i++)
        {
            float a = Mathf.Tau * i / Seg;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        c.DrawColoredPolygon(pts, col);
    }

    private void DrawPowerBar(CanvasItem c)
    {
        var barBg = new Rect2(240, 962, 320, 34);
        c.DrawRect(barBg, new Color(0.08f, 0.09f, 0.08f, 0.8f));
        c.DrawRect(barBg, new Color(0.90f, 0.85f, 0.65f, 0.5f), false, 2f);

        float power = 0f;
        if (dragging && gs == GState.Aim)
        {
            Vector2 pull = dragFrom - dragNow;
            power = Mathf.Clamp(pull.Length() / MaxDrag, 0f, 1f);
        }
        if (power > 0.01f)
        {
            Color fill = Colors.Green.Lerp(Colors.Red, Mathf.Pow(power, 1.4f)) with { A = 0.9f };
            c.DrawRect(new Rect2(barBg.Position + new Vector2(3, 3), new Vector2((barBg.Size.X - 6) * power, barBg.Size.Y - 6)), fill);
        }

        if (drawFont != null)
            c.DrawString(drawFont, new Vector2(240, 950), HudText("power"), HorizontalAlignment.Left, -1, 20, UIDim);
    }

    private void DrawSpinWidget(CanvasItem c)
    {
        Vector2 center = new(1652, 984);
        float r = 40f;

        c.DrawCircle(center, r + 8f, new Color(0.08f, 0.09f, 0.08f, 0.65f));
        c.DrawArc(center, r + 8f, 0f, Mathf.Tau, 40, new Color(0.90f, 0.85f, 0.65f, 0.4f), 2f, true);

        c.DrawCircle(center, r, WhiteBallCol);
        c.DrawCircle(center + new Vector2(-r * 0.3f, -r * 0.36f), r * 0.26f, new Color(1, 1, 1, 0.7f));
        c.DrawArc(center, r, 0f, Mathf.Tau, 40, new Color(0.58f, 0.56f, 0.48f), 1.6f, true);

        // 十字参考线
        c.DrawLine(center + new Vector2(-r * 0.72f, 0), center + new Vector2(r * 0.72f, 0), new Color(0, 0, 0, 0.10f), 1f);
        c.DrawLine(center + new Vector2(0, -r * 0.72f), center + new Vector2(0, r * 0.72f), new Color(0, 0, 0, 0.10f), 1f);

        // 击点
        Vector2 dot = center + spinOff * (r * 0.62f);
        c.DrawCircle(dot, 7.5f, new Color(0.85f, 0.22f, 0.20f));
        c.DrawArc(dot, 7.5f, 0f, Mathf.Tau, 20, new Color(0.55f, 0.12f, 0.10f), 1.2f, true);
    }
}
