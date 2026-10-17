using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 可拖拽的场景物品（第三轮：天平章的「往上压」交互）
///
/// 左右两幅画里的东西可以拿出来：他的泡面桶、书堆；她的咖啡杯、便利贴墙。
/// 按住拖起来（跟手、微放大、带影子），拖到天平附近松手——物品飞上托盘，
/// 天平往它那一侧沉下去，然后弹一段只属于这件东西的回忆。
///
/// 拖空了（离天平太远）：物品自己滑回原位，什么都没发生。
/// </summary>
public partial class DragProp : TextureRect
{
    /// <summary>落盘时回调（参数=自己，由章节处理倾斜/对白/移除）</summary>
    public Action<DragProp> Dropped;

    /// <summary>物品归属：true=他的（左半屏，压左盘）；false=她的（右半屏，压右盘）</summary>
    public bool BelongsToHim = true;

    /// <summary>落盘对白（每件物品专属）</summary>
    public List<DialogueLine> Lines = new();

    private bool dragging;
    private bool done;
    private Vector2 dragOffset;
    private float homeLeft;
    private float homeTop;
    private Func<Vector2> panTarget; // 返回托盘屏幕坐标

    public void Setup(Func<Vector2> panTargetProvider)
    {
        panTarget = panTargetProvider;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        homeLeft = Position.X;
        homeTop = Position.Y;
        // 平时半透明（不抢画面），鼠标扫过才亮起来——「这件东西可以拿」
        Modulate = new Color(1f, 1f, 1f, 0.55f);
        MouseEntered += () => { if (!dragging && !done) Modulate = Colors.White; };
        MouseExited += () => { if (!dragging && !done) Modulate = new Color(1f, 1f, 1f, 0.55f); };
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (done)
            return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                dragging = true;
                dragOffset = GetGlobalMousePosition() - GlobalPosition;
                ZIndex = 30;
                var tw = CreateTween();
                tw.SetParallel(true);
                tw.TweenProperty(this, "scale", new Vector2(1.12f, 1.12f), 0.12);
                tw.TweenProperty(this, "modulate", new Color(1.05f, 1.02f, 0.96f), 0.12);
                AudioManager.Instance?.PlaySfx(AudioManager.SfxTick, -16f, 0.03f, 0.9f);
            }
            else if (dragging)
            {
                dragging = false;
                TryDrop();
            }
        }
        else if (@event is InputEventMouseMotion && dragging)
        {
            // 跟手（转换到父节点坐标）
            var target = GetGlobalMousePosition() - dragOffset;
            Position = GetParent<Control>().GetGlobalTransform().AffineInverse() * target;
        }
    }

    private void TryDrop()
    {
        var target = panTarget?.Invoke() ?? Vector2.Zero;
        float dist = GlobalPosition.DistanceTo(target);
        if (dist < 220f)
        {
            // 落盘：飞向托盘、缩小、消失
            done = true;
            var tw = CreateTween();
            tw.SetParallel(true);
            tw.TweenProperty(this, "global_position", target, 0.32)
              .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
            tw.TweenProperty(this, "scale", new Vector2(0.25f, 0.25f), 0.32);
            tw.TweenProperty(this, "modulate:a", 0f, 0.32);
            tw.Chain().TweenCallback(Callable.From(() =>
            {
                Dropped?.Invoke(this);
                QueueFree();
            }));
        }
        else
        {
            // 拖空了：滑回原位
            var tw = CreateTween();
            tw.SetParallel(true);
            tw.TweenProperty(this, "position", new Vector2(homeLeft, homeTop), 0.35)
              .SetTrans(Tween.TransitionType.Sine);
            tw.TweenProperty(this, "scale", Vector2.One, 0.2);
            tw.TweenProperty(this, "modulate", Colors.White, 0.2);
            ZIndex = 0;
        }
    }
}
