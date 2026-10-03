using Godot;
using System;

/// <summary>
/// DEMO 试玩结束页
/// 显示玩家的好感度 / 勇气值 / 当前结局倾向，然后可以返回主菜单
/// </summary>
public partial class DemoEnd : Control
{
    public override void _Ready()
    {
        var gm = GameManager.Instance;

        var stats = GetNode<Label>("Center/VBox/StatsLabel");
        stats.Text = $"好感度：{gm.Affection}    勇气值：{gm.Courage}\n当前结局倾向：{GetEndingName(gm.GetEndingType())}";

        GetNode<Button>("Center/VBox/BackButton").Pressed += OnBackPressed;

        GD.Print("[DEMO结束] 展示结算数据完成");
    }

    private void OnBackPressed()
    {
        GameManager.Instance.ReturnToMainMenu();
    }

    /// <summary>
    /// 把结局枚举翻译成给玩家看的中文
    /// </summary>
    private static string GetEndingName(EndingType type)
    {
        return type switch
        {
            EndingType.SunnyDay => "晴天（完美结局）",
            EndingType.SouthWind => "南风（温暖结局）",
            EndingType.HermitCrab => "寄居蟹（遗憾结局）",
            EndingType.NowIUnderstand => "开始懂了（隐藏结局）",
            _ => "未知"
        };
    }
}
