using Godot;
using System.Collections.Generic;

/// <summary>
/// 心脏场景（第三轮：《天平》章结尾的心理间奏）
///
/// 她说完「对不起」，走出咖啡馆之后，画面暗下来——不是黑场，是像闭上眼睛。
/// 黑暗里有一颗心在跳。然后她说过的话变成四把刀，一把一把扎进来：
/// 没安全感 / 没时间陪 / 永远不在 / 好闷。
///
/// 每插一把：画面震一下，褐色的旧血从刀口渗开，心跳漏一拍。
/// 四把刀插满后，玩家可以点刀把它拔出来——也可以不拔。
/// 无论拔不拔，那张字都会出现：
///
///     「它还在跳。」
///
/// 这是全书「放过自己」最初的伏笔。
/// </summary>
public partial class HeartBlades : Control
{
    /// <summary>演出结束（拔了几把刀通过参数带回）</summary>
    private readonly System.Action<int> onDone;

    private Texture2D heart;
    private Vector2 heartCenter;
    private float heartScale;

    private float t;
    private const float DarkIn = 0.9f;
    private const float HeartIn = 1.4f;
    private const float BladeInterval = 1.45f;
    private const float BladeFlyIn = 0.42f;
    private const float WaitAfterBlades = 9f;    // 插满后等待（可拔刀；不管也会自己往下走）
    private const float LineShow = 2.6f;
    private const float FadeOut = 0.8f;

    private static readonly string[] Blame =
    {
        "你给不了我安全感。",
        "你从来没时间陪我。",
        "我难受的时候，你永远不在。",
        "跟你在一起，好闷。",
    };

    private class Blade
    {
        public Vector2 Pos;        // 插在心脏上的位置（屏幕坐标）
        public Vector2 Dir;        // 刀尖朝向（用于画刀身）
        public float Angle;        // 刀身角度
        public float InT;          // 飞入进度 0~1
        public float AppearAt;     // 何时开始飞入
        public Vector2 StartFrom;  // 从哪个屏幕外点飞来
        public string Text = "";
        public float TextA;        // 台词透明度
        public bool Pulled;        // 已拔
        public bool Pulling;
        public float PullT;
    }

    private readonly List<Blade> blades = new();
    private readonly List<(Vector2 p, float r, float a)> blood = new();
    private int pulledCount;
    private bool allIn;
    private bool lineShown;
    private float shakeT;
    private float beatSkip;      // 漏拍（0~1，1=刚漏过）

    public HeartBlades(System.Action<int> done)
    {
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        heart = ResourceLoader.Exists("res://assets/art/props/heart_v1.png")
            ? GD.Load<Texture2D>("res://assets/art/props/heart_v1.png") : null;

        var size = GetViewportRect().Size;
        heartCenter = size / 2f;
        heartScale = Mathf.Min(size.Y * 0.52f, size.X * 0.34f) / (heart?.GetHeight() ?? 900f);

        // 刀：绕心脏边缘四个方向飞入（避开正中）
        var dirs = new[]
        {
            (new Vector2(-1f, -0.35f), 0.55f),
            (new Vector2(1f, -0.15f), -0.35f),
            (new Vector2(-0.85f, 0.45f), -0.25f),
            (new Vector2(0.9f, 0.4f), 0.65f),
        };
        var rng = new System.Random(2026);
        for (int i = 0; i < 4; i++)
        {
            var dir = dirs[i].Item1.Normalized();
            var pos = heartCenter + dir * (heartScale * 983f * 0.12f);
            var from = heartCenter + dir * (size.Length() * 0.75f);
            blades.Add(new Blade
            {
                Pos = pos,
                Dir = dir,
                Angle = dirs[i].Item2,
                AppearAt = DarkIn + HeartIn + i * BladeInterval,
                StartFrom = from,
                Text = Blame[i],
            });
        }
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float dt = (float)delta;

        // 插刀推进
        foreach (var b in blades)
        {
            if (t >= b.AppearAt && b.InT < 1f)
            {
                float before = b.InT;
                b.InT = Mathf.Clamp((t - b.AppearAt) / BladeFlyIn, 0f, 1f);
                if (before < 1f && b.InT >= 1f)
                {
                    // 落刀瞬间：震动 + 漏拍 + 血渗开
                    shakeT = 0.32f;
                    beatSkip = 1f;
                    blood.Add((b.Pos, 0f, 0f));
                    AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -8f, 0.05f, 0.75f);
                }
            }
            // 台词：落刀后淡入，停 2 秒淡出
            float since = t - (b.AppearAt + BladeFlyIn);
            if (since > 0f && !b.Pulled)
            {
                b.TextA = since < 0.35f ? since / 0.35f
                    : since < 2.4f ? 1f
                    : Mathf.Max(0f, 1f - (since - 2.4f) / 0.7f);
            }
            else
            {
                b.TextA = 0f;
            }
            // 拔刀动画
            if (b.Pulling)
            {
                b.PullT += dt / 0.55f;
                if (b.PullT >= 1f)
                {
                    b.Pulling = false;
                    b.Pulled = true;
                    pulledCount++;
                    blood.Add((b.Pos, 0f, 0f));
                    beatSkip = 1f;
                    shakeT = 0.25f;
                }
            }
        }

        // 血缓慢扩散
        for (int i = 0; i < blood.Count; i++)
        {
            var (p, r, a) = blood[i];
            blood[i] = (p, Mathf.Min(r + dt * 34f, 90f), Mathf.Min(a + dt * 1.6f, 0.55f));
        }

        if (shakeT > 0f)
            shakeT = Mathf.Max(0f, shakeT - dt);
        if (beatSkip > 0f)
            beatSkip = Mathf.Max(0f, beatSkip - dt * 1.4f);

        // 第三把刀落下时 BGM 停（音乐骤停）
        if (t >= blades[2].AppearAt + BladeFlyIn - dt && t < blades[2].AppearAt + BladeFlyIn)
            AudioManager.Instance?.PlayBgm("", 0f, 0.4f);

        allIn = t >= blades[3].AppearAt + BladeFlyIn;

        // 结束
        float endAt = blades[3].AppearAt + BladeFlyIn + WaitAfterBlades;
        if (!lineShown && t >= endAt)
        {
            lineShown = true;
            AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -22f, 0.05f);
        }
        if (t >= endAt + LineShow + FadeOut)
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke(pulledCount);
            return;
        }
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!allIn || !(@event is InputEventMouseButton mb) || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;
        var p = GetViewport().GetMousePosition();
        foreach (var b in blades)
        {
            if (b.Pulled || b.Pulling)
                continue;
            if (p.DistanceTo(b.Pos) < 52f)
            {
                b.Pulling = true;
                AudioManager.Instance?.PlaySfx(AudioManager.SfxScaleCreak, -12f, 0.05f, 1.5f);
                break;
            }
        }
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var shake = shakeT > 0f
            ? new Vector2((float)GD.RandRange(-4, 4), (float)GD.RandRange(-3, 3)) * (shakeT / 0.32f)
            : Vector2.Zero;

        // 黑底（闭眼后的黑暗，不是纯黑——带一点暖褐）
        float dark = Mathf.Clamp(t / DarkIn, 0f, 1f);
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.055f, 0.035f, 0.03f, dark));

        if (heart == null)
            return;

        // 心脏：搏动（72bpm 左右；漏拍时停一下）
        float beat = 1f;
        if (t > DarkIn + 0.4f)
        {
            float period = 0.84f;
            float phase = (t % period) / period;
            float pulse = Mathf.Sin(phase * Mathf.Pi) * (phase < 0.35f ? 1f : 0.25f);
            beat = 1f + 0.022f * pulse * (1f - beatSkip);
        }
        float appear = Mathf.Clamp((t - DarkIn) / HeartIn, 0f, 1f);
        float s = heartScale * beat;

        var hpos = heartCenter - new Vector2(heart.GetWidth(), heart.GetHeight()) * s / 2f + shake;
        DrawTextureRect(heart, new Rect2(hpos, heart.GetSize() * s), false,
            new Color(1f, 1f, 1f, appear * 0.95f));

        // 血（旧血褐色，缓慢渗开在刀口周围）
        foreach (var (p, r, a) in blood)
        {
            if (r <= 0.5f)
                continue;
            DrawCircle(p + shake, r, new Color(0.32f, 0.10f, 0.07f, a * 0.55f));
            DrawCircle(p + shake, r * 0.55f, new Color(0.24f, 0.07f, 0.05f, a * 0.6f));
        }

        // 刀
        foreach (var b in blades)
        {
            if (b.InT <= 0f)
                continue;
            if (b.Pulled)
                continue;

            Vector2 pos = b.Pos + shake;
            if (b.InT < 1f)
            {
                // 飞入：从 StartFrom 缓动到 Pos（Back ease in）
                float e = 1f - Mathf.Pow(1f - b.InT, 2.4f);
                pos = b.StartFrom.Lerp(b.Pos, e) + shake;
            }
            // 拔出：沿刀身方向往外滑 + 淡出
            float alpha = 1f;
            if (b.Pulling)
            {
                float e = b.PullT * b.PullT;
                pos += b.Dir * (120f * e);
                alpha = 1f - e * 0.85f;
            }

            DrawSetTransform(pos, b.Angle, Vector2.One);
            // 刀身（细长冷青灰）
            var bladeCol = new Color(0.64f, 0.70f, 0.75f, alpha);
            var edgeCol = new Color(0.90f, 0.94f, 0.97f, alpha);
            DrawColoredPolygon(new[]
            {
                new Vector2(0, 0),
                new Vector2(-24, -92),
                new Vector2(24, -92),
            }, new Color(0.16f, 0.18f, 0.20f, alpha * 0.55f)); // 阴影
            DrawColoredPolygon(new[]
            {
                new Vector2(0, 5),
                new Vector2(-17, -88),
                new Vector2(17, -88),
            }, bladeCol);
            DrawLine(new Vector2(0, 2), new Vector2(0, -84), edgeCol, 2.4f); // 中脊
            // 刀格 + 刀柄
            DrawRect(new Rect2(-30, -100, 60, 12), new Color(0.30f, 0.23f, 0.18f, alpha));
            DrawRect(new Rect2(-11, -158, 22, 58), new Color(0.24f, 0.18f, 0.14f, alpha));
            DrawRect(new Rect2(-15, -166, 30, 9), new Color(0.32f, 0.25f, 0.19f, alpha));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            // 台词（刀柄旁边）
            if (b.TextA > 0.01f)
            {
                var font = GetThemeDefaultFont();
                float tw = font.GetStringSize(b.Text, HorizontalAlignment.Left, -1, 26).X;
                var tp = b.Pos + new Vector2(b.Dir.X > 0 ? 46f : -tw - 46f, -186f) + shake;
                DrawString(font, tp, b.Text, HorizontalAlignment.Left, -1, 26,
                    new Color(0.90f, 0.86f, 0.80f, b.TextA));
            }
        }

        // 「点刀可以拔出来」提示（插满后、还没拔的时候）
        if (allIn && pulledCount == 0 && t < blades[3].AppearAt + BladeFlyIn + 6f)
        {
            var font = GetThemeDefaultFont();
            string hint = "（点那些刀，可以拔出来。）";
            var hp = new Vector2(size.X / 2f - 160f, size.Y * 0.88f);
            DrawString(font, hp, hint, HorizontalAlignment.Left, -1, 22,
                new Color(0.75f, 0.72f, 0.68f, 0.75f));
        }

        // 「它还在跳。」
        if (lineShown)
        {
            float a = Mathf.Clamp((t - (blades[3].AppearAt + BladeFlyIn + WaitAfterBlades)) / 1.2f, 0f, 1f);
            var font = GetThemeDefaultFont();
            var lp = new Vector2(size.X / 2f - 62f, size.Y * 0.32f);
            DrawString(font, lp, "它还在跳。", HorizontalAlignment.Left, -1, 34,
                new Color(0.86f, 0.80f, 0.74f, a));
        }
    }
}
