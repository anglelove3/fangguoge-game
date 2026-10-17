using Godot;

/// <summary>
/// 全局交互反馈（第三轮反馈 3）：整局游戏统一的手感。
///
/// 1) 光标：鼠标移到任何可交互控件（Button 系）上变成手型
/// 2) 点击涟漪：鼠标左键按下的地方起一圈水波（RippleFx），任何场景都有
///
/// 挂在 autoload——一处实现，全游戏生效，各章各界面不用各写一遍。
/// </summary>
public partial class GlobalClickFx : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;
        // 只在游戏场景里出涟漪（不在暂停/无场景时）
        var scene = GetTree().CurrentScene;
        if (scene == null)
            return;

        var ripple = new RippleFx
        {
            Position = GetViewport().GetMousePosition(),
            ZIndex = 120,
        };
        scene.AddChild(ripple);
    }
}
