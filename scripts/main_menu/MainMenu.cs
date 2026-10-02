using Godot;
using System;

/// <summary>
/// 主菜单脚本
/// 挂在 main_menu.tscn 的根节点上
/// </summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        // 获取按钮节点并绑定点击事件
        var startButton = GetNode<Button>("VBoxContainer/StartButton");
        var quitButton = GetNode<Button>("VBoxContainer/QuitButton");

        startButton.Pressed += OnStartGamePressed;
        quitButton.Pressed += OnQuitPressed;

        GD.Print("主菜单已加载");
    }

    /// <summary>
    /// 点击"开始游戏"→ 切换到序章
    /// </summary>
    private void OnStartGamePressed()
    {
        GD.Print("→ 开始游戏，进入序章");
        // 后续改为序章场景路径
        GetTree().ChangeSceneToFile("res://scenes/chapters/prologue/prologue.tscn");
    }

    /// <summary>
    /// 点击"退出游戏"
    /// </summary>
    private void OnQuitPressed()
    {
        GD.Print("→ 退出游戏");
        GetTree().Quit();
    }
}
