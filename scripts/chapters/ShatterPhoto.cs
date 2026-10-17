using Godot;
using System.Collections.Generic;

/// <summary>
/// 「删不掉的合照」破碎演出（第三轮：《手滑》章核心重锤）
///
/// 深夜，他翻到相册里他们的第一张合照——大学，两个人挤在一张照片里笑。
/// 他按了删除。删不掉。屏幕上的照片先裂开一道缝，然后整张碎掉，
/// 碎片旋转着落下去，像玻璃渣，也像那天下午的阳光碎了一地。
///
/// 黑场，一行字：「删得掉照片，删不掉那天。」
/// </summary>
public partial class ShatterPhoto : Control
{
    private readonly System.Action onDone;
    private float t;

    private Texture2D photo;
    private TextureRect photoRect;
    private readonly List<(Rect2 region, Vector2 pos, Vector2 vel, float rot, float rotVel)> shards = new();
    private readonly List<(Vector2 a, Vector2 b)> cracks = new();
    private readonly System.Random rng = new();

    private const float Show = 2.2f;     // 静看
    private const float Crack = 0.75f;   // 裂纹扩散
    private const float Shatter = 2.2f;  // 碎片落尽
    private const float Black = 1.1f;    // 黑场
    private const float Line = 2.6f;     // 字幕
    private static float Total => Show + Crack + Shatter + Black + Line;

    private ColorRect dim;
    private Panel frame;
    private Label line;
    private bool shardSpawned;

    public ShatterPhoto(System.Action done)
    {
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var size = GetViewportRect().Size;

        // 压暗背景
        dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // 照片（居中，带白相框感）
        string path = "res://assets/art/props/our_first_photo.png";
        photo = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (photo != null)
        {
            float side = Mathf.Min(size.Y * 0.62f, size.X * 0.42f);
            photoRect = new TextureRect
            {
                Texture = photo,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            photoRect.Size = new Vector2(side, side);
            photoRect.Position = (size - photoRect.Size) / 2f;
            AddChild(photoRect);

            // 相框白边
            frame = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat { BgColor = new Color(0.97f, 0.95f, 0.91f) };
            style.ContentMarginLeft = 10; style.ContentMarginRight = 10;
            style.ContentMarginTop = 10; style.ContentMarginBottom = 10;
            frame.AddThemeStyleboxOverride("panel", style);
            frame.Position = photoRect.Position - new Vector2(10, 10);
            frame.Size = photoRect.Size + new Vector2(20, 20);
            AddChild(frame);
            MoveChild(frame, photoRect.GetIndex()); // 相框在照片下面

            // 生成裂纹（从中心辐射的折线，带分叉感）
            var c = photoRect.Position + photoRect.Size / 2f;
            float r = side * 0.72f;
            for (int i = 0; i < 9; i++)
            {
                float ang = Mathf.Tau * i / 9f + (float)rng.NextDouble() * 0.5f;
                var p1 = c;
                var p2 = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * 0.45f;
                float ang2 = ang + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                var p3 = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * 0.45f
                           + new Vector2(Mathf.Cos(ang2), Mathf.Sin(ang2)) * r * 0.55f;
                cracks.Add((p1, p2));
                cracks.Add((p2, p3));
            }
        }

        // 字幕（黑场后出现）
        line = new Label
        {
            Text = "删得掉照片，删不掉那天。",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        line.AddThemeFontSizeOverride("font_size", 32);
        line.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.86f, 0f));
        line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(line);

        AudioManager.Instance?.PlayBgm("", 0f, 2.0f);
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        float dt = (float)delta;

        // 阶段一：压暗 + 静看
        if (t < Show)
        {
            dim.Color = new Color(0f, 0f, 0f, Mathf.Clamp(t / 0.8f, 0f, 1f) * 0.82f);
        }
        // 阶段二：裂纹扩散（擦玻璃声）
        else if (t < Show + Crack && !shardSpawned)
        {
            if (t - Show < dt * 1.5f)
                AudioManager.Instance?.PlaySfx(AudioManager.SfxScaleCreak, -10f, 0.06f, 1.6f);
            dim.Color = new Color(0f, 0f, 0f, 0.82f);
        }
        // 阶段三：碎裂
        else if (!shardSpawned)
        {
            shardSpawned = true;
            SpawnShards();
            AudioManager.Instance?.PlaySfx(AudioManager.SfxCardFly, -6f, 0.08f, 0.6f);
            if (photoRect != null)
                photoRect.Visible = false;
            if (frame != null)
                frame.Visible = false;
        }

        if (shardSpawned)
        {
            for (int i = 0; i < shards.Count; i++)
            {
                var s = shards[i];
                s.vel = new Vector2(s.vel.X * 0.995f, s.vel.Y + 700f * dt); // 重力
                s.pos += s.vel * dt;
                s.rot += s.rotVel * dt;
                shards[i] = s;
            }
        }

        // 黑场
        float blackStart = Show + Crack + Shatter;
        if (t >= blackStart && t < blackStart + Black)
        {
            dim.Color = new Color(0f, 0f, 0f, Mathf.Lerp(0.82f, 1f, (t - blackStart) / Black));
        }
        // 字幕浮现
        if (t >= blackStart + Black * 0.6f)
        {
            float a = Mathf.Clamp((t - (blackStart + Black * 0.6f)) / 1.2f, 0f, 1f);
            line.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.86f, a));
        }

        if (t >= Total)
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke();
            return;
        }
        QueueRedraw();
    }

    private void SpawnShards()
    {
        if (photo == null || photoRect == null)
            return;
        var origin = photoRect.Position;
        float side = photoRect.Size.X;
        const int Nx = 4, Ny = 4;
        float cw = side / Nx, ch = side / Ny;
        var size = GetViewportRect().Size;
        var center = origin + new Vector2(side / 2f, side / 2f);
        for (int iy = 0; iy < Ny; iy++)
        for (int ix = 0; ix < Nx; ix++)
        {
            var region = new Rect2(ix * cw, iy * ch, cw, ch);
            var pos = origin + new Vector2(ix * cw, iy * ch);
            var dir = (pos + new Vector2(cw, ch) / 2f - center).Normalized();
            var vel = dir * (60f + (float)rng.NextDouble() * 180f) + new Vector2(0, -80f);
            shards.Add((region, pos, vel,
                (float)(rng.NextDouble() * 0.6 - 0.3),
                (float)(rng.NextDouble() * 6 - 3)));
        }
    }

    public override void _Draw()
    {
        if (photo == null || photoRect == null)
            return;

        // 碎片
        if (shardSpawned)
        {
            float age = t - (Show + Crack);
            float alpha = Mathf.Clamp(1f - (age - 1.2f) / 1f, 0f, 1f);
            if (alpha <= 0.01f)
                return;
            foreach (var s in shards)
            {
                DrawSetTransform(s.pos, s.rot, Vector2.One);
                DrawTextureRectRegion(photo, new Rect2(Vector2.Zero, s.region.Size), s.region,
                    new Color(1f, 1f, 1f, alpha));
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return;
        }

        // 裂纹（逐条出现）
        float crackT = Mathf.Clamp((t - Show) / Crack, 0f, 1f);
        int shown = (int)(crackT * cracks.Count);
        for (int i = 0; i < shown && i < cracks.Count; i++)
        {
            var (a, b) = cracks[i];
            DrawLine(a, b, new Color(1f, 1f, 1f, 0.85f), 2.2f);
            DrawLine(a, b, new Color(0.1f, 0.1f, 0.12f, 0.5f), 1f);
        }
    }
}
