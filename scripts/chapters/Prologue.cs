using Godot;
using System;

/// <summary>
/// 序章：那首歌
/// 继承 ChapterBase，自动拥有标题显示、返回菜单、自动存档等功能
///
/// 【Day 2 知识点 - 继承的实际使用】
/// 注意这里写的是 : ChapterBase 而不是 : Control
/// ChapterBase 本身继承了 Control，所以 Prologue 也是 Control
/// 这就是"继承链"：Prologue → ChapterBase → Control → Node → Object
/// </summary>
public partial class Prologue : ChapterBase
{
    /// <summary>
    /// 重写基类的虚方法，写序章特有的逻辑
    /// </summary>
    protected override void OnChapterReady()
    {
        GD.Print("序章：那首歌 —— 深夜实验室，循环播放《开始懂了》");
        // 后续在这里添加序章特有的互动逻辑：
        // - 点击实验室物品触发对话
        // - 打字机效果显示旁白
        // - 引出"放过哥"外号的选择
    }
}
