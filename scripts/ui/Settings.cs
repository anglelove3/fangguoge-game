using Godot;
using System;

/// <summary>
/// 设置界面
///
/// 【知识点 - DisplayServer（显示服务器）】
/// Godot 用 DisplayServer 这个"系统接口"控制窗口本身：
///   - DisplayServer.WindowGetMode()  → 查询当前是全屏还是窗口
///   - DisplayServer.WindowSetMode()  → 切换全屏/窗口
/// 注意：这个设置目前只在本次运行中生效（重启游戏会恢复），
/// 保存设置到文件的功能后续版本再加（对应计划的 Day 24）。
/// </summary>
public partial class Settings : Control
{
    private CheckButton fullscreenCheck;
    private Button backButton;

    public override void _Ready()
    {
        fullscreenCheck = GetNode<CheckButton>("CenterContainer/VBox/FullscreenCheck");
        backButton = GetNode<Button>("CenterContainer/VBox/BackButton");

        // 根据当前窗口状态初始化复选框
        bool isFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
        fullscreenCheck.ButtonPressed = isFullscreen;

        // 绑定事件
        fullscreenCheck.Toggled += OnFullscreenToggled;
        backButton.Pressed += OnBackPressed;

        // 按钮音效
        UiSounds.WireAll(this);

        GD.Print("[设置界面] 已加载");
    }

    /// <summary>
    /// 全屏开关切换
    /// </summary>
    private void OnFullscreenToggled(bool pressed)
    {
        if (pressed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            GD.Print("[设置] 切换到全屏");
        }
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            GD.Print("[设置] 切换到窗口模式");
        }
    }

    /// <summary>
    /// 返回主菜单
    /// </summary>
    private void OnBackPressed()
    {
        GD.Print("→ 返回主菜单");
        GameManager.Instance.ChangeSceneWithTransition("res://scenes/main_menu/main_menu.tscn");
    }
}
