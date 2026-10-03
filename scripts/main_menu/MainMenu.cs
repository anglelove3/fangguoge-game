using Godot;
using System;

/// <summary>
/// 主菜单脚本
/// 挂在 main_menu.tscn 的根节点上
///
/// 【Day 2 知识点 - 信号绑定】
/// Godot 中按钮点击会发出 "pressed" 信号，
/// C# 中用 += 把这个信号连接到我们的方法上。
/// 就像：当门铃响了(pressed)，就去开门(OnXxxPressed)。
/// </summary>
public partial class MainMenu : Control
{
    // 声明继续游戏按钮（可能不存在，取决于有没有存档）
    private Button continueButton;

    public override void _Ready()
    {
        // 获取按钮节点
        var startButton = GetNode<Button>("VBoxContainer/StartButton");
        var settingsButton = GetNode<Button>("VBoxContainer/SettingsButton");
        var quitButton = GetNode<Button>("VBoxContainer/QuitButton");

        // 检查是否有存档，决定"继续游戏"按钮是否可用
        continueButton = GetNodeOrNull<Button>("VBoxContainer/ContinueButton");
        if (continueButton != null)
        {
            if (GameManager.Instance.HasSaveFile())
            {
                continueButton.Visible = true;
                continueButton.Pressed += OnContinueGamePressed;
            }
            else
            {
                continueButton.Visible = false;
            }
        }

        // 绑定按钮事件
        startButton.Pressed += OnStartGamePressed;
        settingsButton.Pressed += OnSettingsPressed;
        quitButton.Pressed += OnQuitPressed;

        // 给所有按钮挂上"悬停/点击"音效
        UiSounds.WireAll(this);

        GD.Print("主菜单已加载");
    }

    /// <summary>
    /// 点击"开始游戏"→ 重置数据，进入序章
    /// </summary>
    private void OnStartGamePressed()
    {
        GD.Print("→ 开始新游戏");
        GameManager.Instance.ResetGame();
        GameManager.Instance.ChangeSceneWithTransition(
            "res://scenes/chapters/prologue/prologue.tscn",
            chapterIndex: 0
        );
    }

    /// <summary>
    /// 点击"继续游戏"→ 读取存档，进入对应章节
    /// </summary>
    private void OnContinueGamePressed()
    {
        GD.Print("→ 继续游戏");
        bool loaded = GameManager.Instance.LoadGame();
        if (loaded)
        {
            string scenePath = GetChapterScenePath(GameManager.Instance.CurrentChapter);
            GameManager.Instance.ChangeSceneWithTransition(scenePath);
        }
    }

    /// <summary>
    /// 点击"设置"→ 打开设置界面
    /// </summary>
    private void OnSettingsPressed()
    {
        GD.Print("→ 打开设置");
        GameManager.Instance.ChangeSceneWithTransition("res://scenes/ui/settings/settings.tscn");
    }

    /// <summary>
    /// 点击"退出游戏"
    /// </summary>
    private void OnQuitPressed()
    {
        GD.Print("→ 退出游戏");
        GetTree().Quit();
    }

    /// <summary>
    /// 根据章节编号返回对应的场景路径
    /// 后续添加新章节时在这里加一行就行
    /// </summary>
    private string GetChapterScenePath(int chapter)
    {
        return chapter switch
        {
            0 => "res://scenes/chapters/prologue/prologue.tscn",
            1 => "res://scenes/chapters/ch01/ch01.tscn",
            2 => "res://scenes/chapters/ch02/ch02.tscn",
            99 => "res://scenes/ui/demo_end/demo_end.tscn", // 试玩结束页
            // 后续章节在这里添加：
            // 3 => "res://scenes/chapters/ch03/xxx/ch03.tscn",
            _ => "res://scenes/chapters/prologue/prologue.tscn"
        };
    }
}
