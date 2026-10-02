using Godot;
using System;

/// <summary>
/// 游戏全局管理器（自动加载单例）
/// 负责场景切换、游戏状态管理
/// 在 Godot 编辑器中：项目 → 项目设置 → 自动加载 → 添加此脚本
/// </summary>
public partial class GameManager : Node
{
    // ========== 游戏属性 ==========
    public int Affection { get; set; } = 0;  // 好感度（0-100），影响三点水姑娘的态度
    public int Courage { get; set; } = 0;    // 勇气值（0-100），影响放过哥是否勇敢

    // 当前章节编号
    public int CurrentChapter { get; set; } = 0;

    // 隐藏物品收集
    public int HiddenItemsFound { get; set; } = 0;
    public int TotalHiddenItems = 10;  // 总共10个隐藏物品

    public override void _Ready()
    {
        GD.Print("===== 放过哥游戏启动 =====");
        GD.Print($"GameManager 已就绪");
    }

    // ========== 场景切换 ==========

    /// <summary>
    /// 切换到指定场景路径
    /// </summary>
    public void ChangeScene(string scenePath)
    {
        GD.Print($"切换场景 → {scenePath}");
        GetTree().ChangeSceneToFile(scenePath);
    }

    // ========== 属性修改 ==========

    /// <summary>
    /// 增加好感度（负数则减少）
    /// </summary>
    public void AddAffection(int value)
    {
        Affection = Mathf.Clamp(Affection + value, 0, 100);
        GD.Print($"❤ 好感度: {Affection} ({(value >= 0 ? "+" : "")}{value})");
    }

    /// <summary>
    /// 增加勇气值（负数则减少）
    /// </summary>
    public void AddCourage(int value)
    {
        Courage = Mathf.Clamp(Courage + value, 0, 100);
        GD.Print($"★ 勇气值: {Courage} ({(value >= 0 ? "+" : "")}{value})");
    }

    /// <summary>
    /// 找到一个隐藏物品
    /// </summary>
    public void FoundHiddenItem()
    {
        HiddenItemsFound++;
        GD.Print($" 隐藏物品: {HiddenItemsFound}/{TotalHiddenItems}");
    }

    // ========== 结局判定 ==========

    /// <summary>
    /// 根据属性值判定结局编号
    /// 1=晴天(完美) 2=南风(温暖) 3=寄居蟹(遗憾) 4=开始懂了(隐藏)
    /// </summary>
    public int GetEndingType()
    {
        if (HiddenItemsFound >= TotalHiddenItems && Affection < 50)
            return 4; // 隐藏结局
        if (Affection >= 70 && Courage >= 70)
            return 1; // 完美结局
        if (Affection >= 50 && Courage >= 50)
            return 2; // 温暖结局
        return 3; // 遗憾结局
    }
}
