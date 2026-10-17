using Godot;
using System.Collections.Generic;

/// <summary>
/// 第四章《星月》——拉扯演出里的「闪回」（第三轮完善）
///
/// 抓着她的手不放的时候，回忆会自己涌上来：
/// 上下分屏两层旧照片（老照片滤镜：暖旧色调+晕影+白边）——
/// 上面是图书馆初见，下面是她说「以后来我的城市」的那个傍晚。
/// 停两秒半，然后碎成水彩纸屑，飘散。
///
/// 素材零新增：复用宿舍夜景/实验室白天两张图，靠 modulate 暖旧化。
/// </summary>
public partial class FlashbackMemory : Control
{
    private float t;
    private bool done;
    private bool shattered;
    private readonly List<(Vector2 Pos, Vector2 Vel, float Rot, float RotVel, Color Col)> shards = new();
    private readonly System.Random rng = new();

    private const float FadeIn = 1.0f;     // 淡入
    private const float Hold = 2.6f;       // 停住（足够看清两张照片）
    private const float Shatter = 1.6f;    // 碎散
    private static float Total => FadeIn + Hold + Shatter;

    private TextureRect topPhoto, bottomPhoto;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        topPhoto = MakePhoto("res://assets/art/backgrounds/dorm_night_v2.png", true);
        bottomPhoto = MakePhoto("res://assets/art/backgrounds/day_lab_v1.png", false);
    }

    /// <summary>老照片层：半屏高、暖旧色调、中间留白边（像翻相册）</summary>
    private TextureRect MakePhoto(string path, bool top)
    {
        var rect = new TextureRect
        {
            Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            // 暖旧滤镜：红绿抬一点、蓝压一点 + 整体降明度
            Modulate = new Color(1.12f, 1.02f, 0.80f, 0f),
        };
        rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (top)
        {
            rect.AnchorBottom = 0.48f;
            rect.OffsetBottom = 0;
        }
        else
        {
            rect.AnchorTop = 0.52f;
            rect.OffsetTop = 0;
        }
        AddChild(rect);
        return rect;
    }

    public override void _Process(double delta)
    {
        if (done)
            return;
        t += (float)delta;

        float fade = Mathf.Clamp(t / FadeIn, 0f, 1f);
        float shatterStart = FadeIn + Hold;

        if (t < shatterStart)
        {
            float a = fade;
            if (t > shatterStart - 0.4f) // 碎前轻轻一亮（最后的显影）
                a = Mathf.Min(1f, a + (t - (shatterStart - 0.4f)) * 0.5f);
            topPhoto.Modulate = new Color(1.12f, 1.02f, 0.80f, a);
            bottomPhoto.Modulate = topPhoto.Modulate;
        }
        else if (!shattered)
        {
            // 碎裂瞬间：生成纸屑
            shattered = true;
            AudioManager.Instance?.PlaySfx(AudioManager.SfxCardFly, -10f, 0.06f, 0.9f);
            topPhoto.Visible = false;
            bottomPhoto.Visible = false;
            var size = GetViewportRect().Size;
            for (int i = 0; i < 90; i++)
            {
                shards.Add((
                    new Vector2((float)rng.NextDouble() * size.X, (float)rng.NextDouble() * size.Y),
                    new Vector2((float)(rng.NextDouble() * 2 - 1) * 60f, 40f + (float)rng.NextDouble() * 90f),
                    (float)rng.NextDouble() * Mathf.Tau,
                    (float)(rng.NextDouble() * 4 - 2),
                    rng.NextDouble() > 0.5
                        ? new Color(0.93f, 0.88f, 0.72f, 0.9f)   // 暖旧纸色
                        : new Color(0.82f, 0.80f, 0.76f, 0.85f)));
            }
        }

        if (shattered)
        {
            float dt = (float)delta;
            for (int i = 0; i < shards.Count; i++)
            {
                var s = shards[i];
                s.Pos += s.Vel * dt;
                s.Vel = new Vector2(s.Vel.X * 0.985f, s.Vel.Y + 26f * dt); // 慢慢往下飘
                s.Rot += s.RotVel * dt;
                shards[i] = s;
            }
        }

        if (t >= Total)
        {
            done = true;
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        if (!shattered)
        {
            // 相册底（深色）+ 两张照片之间的白边 + 晕影
            DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.10f, 0.08f, 0.06f, 0.75f));
            float midY = size.Y * 0.5f;
            DrawRect(new Rect2(0, midY - 10, size.X, 20), new Color(0.94f, 0.92f, 0.86f, 0.85f));
            // 晕影（四角压暗，老照片感）
            DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.06f, 0.04f, 0.03f, 0.18f));
            return;
        }

        // 纸屑飘散
        float p = (t - FadeIn - Hold) / Shatter;
        float alpha = 1f - p * p;
        foreach (var s in shards)
        {
            var col = new Color(s.Col, s.Col.A * alpha);
            DrawSetTransform(s.Pos, s.Rot, Vector2.One);
            DrawRect(new Rect2(-4f, -3f, 8f, 6f), col);
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // 碎完后画面回到夜的底色
        DrawRect(new Rect2(0, 0, size.X, size.Y), new Color(0.03f, 0.03f, 0.06f, 0.25f * (1f - p)));
    }
}
