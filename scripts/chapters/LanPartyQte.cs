using Godot;
using System;

/// <summary>
/// 开黑节奏小游戏（第十五章）：三拍判定。
///
/// 【规则】一条横轴上一个光标来回跑，中间有一段蓝色判定区（每拍位置随机）。
/// 光标进入蓝区时按 空格 / 点鼠标 —— 命中三拍里两拍以上算赢。
///
/// 【为什么做成"三拍"而不是完整音游】
/// 这一章的戏在"她靠过来"那一刻，不在小游戏本身。给它三拍（约 10 秒），
/// 让玩家手上有点事做、又不会把注意力从剧情上拽走；三拍的速度递增
/// （0.85 → 1.10 → 1.35 单位/秒），第三拍会稍微紧张一下。
/// </summary>
public partial class LanPartyQte : Control
{
    private const int TotalBeats = 3;
    private const float ZoneHalfWidth = 0.085f;   // 判定区半宽（归一化）
    private const float MarkerSpeed0 = 0.85f;

    private int beatIndex;
    private int hits;
    private float marker = 0f;
    private float dir = 1f;
    private float zoneCenter = 0.5f;
    private float speed = MarkerSpeed0;

    private bool waitingNextBeat;
    private float beatCooldown;
    private float flashTimer;
    private bool lastHit;
    private bool running;

    private Action<int> onFinished;
    private Label tipLabel;

    /// <summary>三拍打完：参数是命中数（0~3）</summary>
    public void Begin(Action<int> onDone)
    {
        onFinished = onDone;
        beatIndex = 0;
        hits = 0;
        running = true;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        PickZone();
        marker = 0f;
        dir = 1f;
        waitingNextBeat = false;
        flashTimer = 0f;

        tipLabel ??= BuildTip();
        tipLabel.Text = "光标进蓝区就按 — 空格 / 点击";
        QueueRedraw();
    }

    private Label BuildTip()
    {
        var l = new Label
        {
            Name = "QteTip",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.SetAnchorsPreset(LayoutPreset.FullRect);
        l.AddThemeFontSizeOverride("font_size", 22);
        l.AddThemeColorOverride("font_color", new Color("dbeaf7"));
        l.AddThemeColorOverride("font_outline_color", new Color(0.03f, 0.05f, 0.08f, 1));
        l.AddThemeConstantOverride("outline_size", 8);
        AddChild(l);
        return l;
    }

    private void PickZone()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        zoneCenter = 0.26f + rng.Randf() * 0.48f;
    }

    public override void _Process(double delta)
    {
        if (!running)
            return;

        if (flashTimer > 0f)
            flashTimer -= (float)delta;

        if (waitingNextBeat)
        {
            beatCooldown -= (float)delta;
            if (beatCooldown > 0f)
            {
                QueueRedraw();
                return;
            }
            waitingNextBeat = false;
            beatIndex++;
            if (beatIndex >= TotalBeats)
            {
                Finish();
                return;
            }
            PickZone();
            speed = MarkerSpeed0 + beatIndex * 0.25f;
            marker = 0f;
            dir = 1f;
            tipLabel.Text = $"第 {beatIndex + 1} / {TotalBeats} 拍";
            QueueRedraw();
            return;
        }

        marker += dir * speed * (float)delta;
        if (marker >= 1f) { marker = 1f; dir = -1f; }
        else if (marker <= 0f) { marker = 0f; dir = 1f; }
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!running || waitingNextBeat)
            return;

        bool pressed =
            (@event is InputEventKey k && k.Pressed && !k.Echo &&
             (k.Keycode == Key.Space || k.Keycode == Key.Enter)) ||
            (@event is InputEventMouseButton m && m.Pressed && m.ButtonIndex == MouseButton.Left);

        if (!pressed)
            return;

        GetViewport().SetInputAsHandled();
        Judge();
    }

    private void Judge()
    {
        bool hit = Mathf.Abs(marker - zoneCenter) <= ZoneHalfWidth;
        lastHit = hit;
        if (hit) hits++;

        tipLabel.Text = hit ? "命中" : "错过";
        flashTimer = 0.42f;
        waitingNextBeat = true;
        beatCooldown = 0.42f;
        QueueRedraw();
    }

    private void Finish()
    {
        running = false;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
        onFinished?.Invoke(hits);
    }

    public override void _Draw()
    {
        if (!running)
            return;

        var s = Size;
        float barY = s.Y * 0.62f;
        float barH = 26f;
        float left = s.X * 0.18f;
        float width = s.X * 0.64f;

        // 底板
        DrawRect(new Rect2(left - 10, barY - 34, width + 20, barH + 68), new Color(0.02f, 0.04f, 0.07f, 0.72f));
        DrawRect(new Rect2(left, barY, width, barH), new Color("101820"));
        DrawRect(new Rect2(left, barY, width, barH), new Color(1, 1, 1, 0.06f), filled: false, width: 1.5f);

        // 判定区
        float zx = left + (zoneCenter - ZoneHalfWidth) * width;
        float zw = ZoneHalfWidth * 2f * width;
        DrawRect(new Rect2(zx, barY - 4, zw, barH + 8), new Color(0.35f, 0.72f, 0.95f, 0.35f));
        DrawRect(new Rect2(zx, barY - 4, zw, barH + 8), new Color("7fd0f5"), filled: false, width: 1.6f);

        // 光标
        float mx = left + marker * width;
        DrawRect(new Rect2(mx - 2, barY - 12, 4, barH + 24), new Color("eaf6ff"));
        DrawRect(new Rect2(mx - 9, barY - 12, 18, 4), new Color(0.92f, 0.97f, 1f, 0.5f));

        // 命中反馈
        if (flashTimer > 0f)
        {
            var c = lastHit ? new Color("8ff0b0") : new Color("f0a0a0");
            DrawRect(new Rect2(left, barY + barH + 8, width * (flashTimer / 0.42f), 3), new Color(c, 0.8f));
        }

        // 进度点：三拍
        for (int i = 0; i < TotalBeats; i++)
        {
            float px = left + width * (0.5f + (i - 1) * 0.045f) - 4;
            var col = i < beatIndex ? new Color("8ff0b0") : new Color(1, 1, 1, 0.25f);
            DrawRect(new Rect2(px, barY - 26, 8, 8), col);
        }
    }
}
