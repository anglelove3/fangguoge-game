using Godot;

/// <summary>
/// 「心之钥匙」（第三轮：天平章开场的隐藏选择）
///
/// 他坐下来，把钥匙串放在桌上——推到她那边：
/// 实验室的、宿舍的、柜子的，还有心里那把。
///
/// 在她说第一句话之前，屏幕上的钥匙还能点。点一下，就拿回来。
/// 没有提示、没有选项框——只有一串钥匙躺在桌上，你还可以反悔。
///
/// 拿回来 → 她愣住，最狠的那句话她说不出口（心脏场景少一把刀）
/// 不拿 → 钥匙在桌上放四秒，然后被她收走
/// </summary>
public partial class KeyOffer : Control
{
    private readonly System.Action<bool> onDone; // true = 收回

    private TextureRect keys;
    private float t;
    private bool resolved;
    private bool takenBack;
    private Vector2 home;
    private float glow;

    private const float DecideTime = 4.5f; // 超过这个时间 = 默认交出去

    public KeyOffer(System.Action<bool> done)
    {
        onDone = done;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var size = GetViewportRect().Size;
        string path = "res://assets/art/props/keys_v1.png";
        if (ResourceLoader.Exists(path))
        {
            float k = Mathf.Min(size.X * 0.22f, 220f) / 749f;
            var tex = GD.Load<Texture2D>(path);
            keys = new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                Size = new Vector2(749f, 642f) * k,
                PivotOffset = new Vector2(749f, 642f) * k / 2f,
            };
            home = new Vector2(size.X / 2f - keys.Size.X / 2f, size.Y * 0.62f);
            keys.Position = home;
            keys.Modulate = new Color(1f, 1f, 1f, 0f);
            AddChild(keys);

            keys.GuiInput += e =>
            {
                if (resolved)
                    return;
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    takenBack = true;
                    Resolve(true);
                }
            };
        }
        else
        {
            Resolve(false);
        }
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (resolved)
            return;

        // 钥匙淡入 + 微弱呼吸光（提示可交互，但不给文字）
        if (keys != null)
        {
            float a = Mathf.Clamp(t / 0.9f, 0f, 1f);
            glow = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
            keys.Modulate = new Color(1f + 0.06f * glow, 1f + 0.04f * glow, 1f, a);
            // 悬停时轻轻放大一点（MouseEntered/Exited 之外的手感）
            var mouse = GetViewport().GetMousePosition();
            bool hover = new Rect2(keys.GlobalPosition, keys.Size).HasPoint(mouse);
            keys.Scale = keys.Scale.Lerp(hover ? new Vector2(1.06f, 1.06f) : Vector2.One, 0.25f);
        }

        if (t >= DecideTime)
            Resolve(false);
    }

    private void Resolve(bool back)
    {
        if (resolved)
            return;
        resolved = true;

        if (keys != null && back)
        {
            // 收回：缩回去、往画面下方（他的口袋）飞
            var tw = CreateTween();
            tw.SetParallel(true);
            tw.TweenProperty(keys, "scale", new Vector2(0.2f, 0.2f), 0.5)
              .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
            tw.TweenProperty(keys, "position", home + new Vector2(0, 260), 0.5);
            tw.TweenProperty(keys, "modulate:a", 0f, 0.5);
            AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -14f, 0.05f);
        }
        else if (keys != null)
        {
            // 没拿：她把钥匙收走（往画面上方偏右滑出）
            var tw = CreateTween();
            tw.SetParallel(true);
            tw.TweenProperty(keys, "position", home + new Vector2(220f, -180f), 0.7);
            tw.TweenProperty(keys, "modulate:a", 0f, 0.7);
            AudioManager.Instance?.PlaySfx(AudioManager.SfxMetalTap, -12f, 0.05f);
        }

        var timer = GetTree().CreateTimer(0.7);
        timer.Timeout += () =>
        {
            var cb = onDone;
            QueueFree();
            cb?.Invoke(back);
        };
    }
}
