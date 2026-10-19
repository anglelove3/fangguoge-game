using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 场景过渡控制器（淡入淡出效果）
///
/// 【Day 2 知识点 - Tween 动画】
/// Tween（补间动画）就是"从 A 平滑变化到 B"：
///   - 透明度从 0 → 1 = 淡入（画面变黑）
///   - 透明度从 1 → 0 = 淡出（画面变清）
/// 不用一帧帧画，Godot 自动帮你算中间的每一帧
///
/// 【2026-10-19 切章卡顿修复】
/// 症状：试玩反馈"不同章节的链接有卡顿"——淡出/淡入时画面僵一下再弹出来。
/// 实测（headless，冷缓存）第三章 1395ms、第十二章 1296ms、第七章 1010ms 的主线程停顿，
/// 其余章也有 200~650ms。这段时间全花在"读 .tscn → 解码 1536×1024 背景 PNG → 建节点"上，
/// 而它正好压在 0.4s 的淡出里，看起来就是一刀一顿。
///
/// 现在两层处理：
///   1) Prefetch(path)：把场景连同依赖贴图丢到后台线程解码（ResourceLoader.LoadThreadedRequest），
///      章内待够 2.5 秒后就把"下一章"先热起来——玩家还在看剧情，解码已经在别的线程做完了；
///   2) FadeToScene 切场前确认预载完成，切完多留 0.12s 黑屏再淡出——把"建节点"的那一两帧藏进黑里。
/// </summary>
public partial class TransitionManager : CanvasLayer
{
    private static TransitionManager instance;
    private ColorRect fadeRect;

    /// <summary>已经请求过线程加载的场景（路径）。只用来去重，不做缓存失效。</summary>
    private static readonly HashSet<string> requested = new();

    public override void _Ready()
    {
        instance = this;
        Layer = 100; // 确保在最上层

        // 创建一个覆盖全屏的黑色矩形
        fadeRect = new ColorRect();
        fadeRect.Color = new Color(0, 0, 0, 0); // 初始完全透明
        AddChild(fadeRect);

        // 设置全屏
        fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore; // 不阻挡点击
    }

    // ==================== 预载（2026-10-19） ====================

    /// <summary>
    /// 把场景丢给后台线程加载。可以重复调用（第二次直接返回）。
    /// 注意：这里只做"请求"，真正取用还是交给 ChangeSceneToFile——届时资源已在缓存里，
    /// 那一句就从"读盘 + 解码"退化成"实例化"。
    /// </summary>
    public static void Prefetch(string scenePath)
    {
        if (instance == null || string.IsNullOrEmpty(scenePath))
            return;
        if (requested.Contains(scenePath))
            return;
        if (!ResourceLoader.Exists(scenePath))
        {
            GD.PrintErr($"[过渡] 预载失败，场景不存在：{scenePath}");
            return;
        }

        // useSubThreads: true —— 让贴图解码也下放到子线程，否则大图还是会回主线程解
        var err = ResourceLoader.LoadThreadedRequest(scenePath, "", true);
        if (err == Error.Ok || err == Error.Busy)
        {
            requested.Add(scenePath);
            GD.Print($"[过渡] 后台预载：{scenePath}");
        }
        else
        {
            GD.Print($"[过渡] 预载请求被拒（{err}）：{scenePath}（不影响正常切章）");
        }
    }

    /// <summary>等预载完成；没预载过就立刻返回。超时也返回——宁可切得慢一点，不能卡死。</summary>
    private static async Task WaitPrefetch(string scenePath, double timeoutSec)
    {
        if (instance == null || !requested.Contains(scenePath))
            return;

        ulong t0 = Time.GetTicksMsec();
        while (true)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(scenePath);
            if (status != ResourceLoader.ThreadLoadStatus.InProgress)
                return; // Loaded / Failed / InvalidResource 都别在这儿耗着
            if ((Time.GetTicksMsec() - t0) / 1000.0 > timeoutSec)
            {
                GD.Print($"[过渡] 预载超时（{timeoutSec:0.0}s），直接切：{scenePath}");
                return;
            }
            await instance.ToSignal(instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // ==================== 淡入淡出 ====================

    /// <summary>
    /// 淡入效果（画面逐渐变黑），然后切换场景，再淡出
    /// </summary>
    public static async void FadeToScene(string scenePath)
    {
        if (instance == null)
        {
            return;
        }

        var rect = instance.fadeRect;
        var tree = instance.GetTree();

        // 第 0 步：现在就发起预载（与淡入并行；上一章如果已经预热过，这里是空操作）
        Prefetch(scenePath);

        // 第一阶段：淡入（画面变黑）
        rect.MouseFilter = Control.MouseFilterEnum.Stop; // 过渡期间阻挡点击
        var tweenIn = instance.CreateTween();
        tweenIn.TweenProperty(rect, "color", new Color(0, 0, 0, 1), 0.35f);
        await instance.ToSignal(tweenIn, Tween.SignalName.Finished);

        // 第一·五阶段：确认后台解码真的做完了（最多等 2.5 秒）
        await WaitPrefetch(scenePath, 2.5);

        // 第二阶段：切换场景（资源已在缓存里，这句只剩实例化）
        tree.ChangeSceneToFile(scenePath);

        // 等两帧让新场景建完节点、跑完 _Ready
        await instance.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await instance.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        // 第二·五阶段：黑屏多停一拍——首帧的布局/字体/音频初始化都藏在这里。
        // 用 Timer（跟着帧走）而不是 sleep：第三章这类首帧要 1 秒的章节，
        // 这一秒会被计时器吃掉，淡出仍然从一帧"干净"的画面开始。
        await instance.ToSignal(tree.CreateTimer(0.28), SceneTreeTimer.SignalName.Timeout);

        // 第三阶段：淡出（画面变清）
        var tweenOut = instance.CreateTween();
        tweenOut.TweenProperty(rect, "color", new Color(0, 0, 0, 0), 0.4f);
        await instance.ToSignal(tweenOut, Tween.SignalName.Finished);

        rect.MouseFilter = Control.MouseFilterEnum.Ignore; // 恢复点击
    }

    /// <summary>
    /// 简单的淡入效果（开场用）
    /// </summary>
    public static async void FadeIn()
    {
        if (instance == null) return;

        var rect = instance.fadeRect;
        rect.Color = new Color(0, 0, 0, 1); // 先全黑

        var tween = instance.CreateTween();
        tween.TweenProperty(rect, "color", new Color(0, 0, 0, 0), 0.8f);
        await instance.ToSignal(tween, Tween.SignalName.Finished);
    }
}
