using Godot;

/// <summary>
/// 指尖涟漪（第三轮 B2）：点击位置散开两圈暖白水波——
/// 「点下去」从瞬时触发变成一次「触碰」。0.55 秒自毁，纯代码。
/// </summary>
public partial class RippleFx : Node2D
{
    private float t;
    private const float Dur = 0.55f;

    public override void _Ready()
    {
        ZIndex = 40;
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t >= Dur)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float p = t / Dur;
        float a = (1f - p) * 0.85f;
        // 两圈：外圈先行、内圈追上来（像真的水波）
        DrawArc(Vector2.Zero, 10f + 44f * EaseOut(p), 0f, Mathf.Tau, 32,
            new Color(1f, 0.97f, 0.9f, a), 2.6f);
        DrawArc(Vector2.Zero, 4f + 30f * EaseOut(Mathf.Max(0f, p - 0.12f) / 0.88f), 0f, Mathf.Tau, 28,
            new Color(1f, 0.99f, 0.94f, a * 0.8f), 1.8f);
    }

    private static float EaseOut(float x) => 1f - Mathf.Pow(1f - x, 3f);
}
