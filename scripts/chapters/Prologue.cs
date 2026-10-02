using Godot;
using System;

/// <summary>
/// 序章场景脚本
/// </summary>
public partial class Prologue : Control
{
    public override void _Ready()
    {
        GD.Print("序章场景已加载");
    }

    /// <summary>
    /// 返回主菜单
    /// </summary>
    private void OnBackButtonPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu/main_menu.tscn");
    }
}
