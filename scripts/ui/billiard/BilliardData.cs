using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// 台球小游戏的数据（第十五轮）
///
/// 【这是什么】
/// 第七章"罗曼蒂克"的台球桌从"两杆固定演出"升级成了一局真能打的球：
/// 拖动出杆（力度 + 杆法）、轮流击球、江洁会点评、赢了输了有彩蛋台词。
/// 桌上那些台词和界面标签全在 data/billiard/billiard.json 里，
/// 和微信、手机桌面一个规矩：改文案不碰代码。
/// </summary>
public static class BilliardData
{
    // ==================== 数据结构 ====================

    public class CommentLine
    {
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";
        public float Hold { get; set; } = 2.4f; // 这句在屏幕上停多久（秒）
    }

    public class HudText
    {
        public string ScoreMe { get; set; } = "";
        public string ScoreHer { get; set; } = "";
        public string YourTurn { get; set; } = "";
        public string HerTurn { get; set; } = "";
        public string ShotN { get; set; } = "";
        public string Power { get; set; } = "";
        public string Spin { get; set; } = "";
        public string SpinCenter { get; set; } = "";
        public string SpinHigh { get; set; } = "";
        public string SpinLow { get; set; } = "";
        public string SpinLeft { get; set; } = "";
        public string SpinRight { get; set; } = "";
        public string AimHint { get; set; } = "";
        public string SheThinks { get; set; } = "";
        public string ClickContinue { get; set; } = "";
    }

    public class BilliardFile
    {
        public string Title { get; set; } = "";
        public HudText Hud { get; set; } = new();
        public Dictionary<string, List<CommentLine>> Comments { get; set; } = new();
    }

    // ==================== 读取 ====================

    private static BilliardFile data;

    public static BilliardFile Data =>
        data ??= DataStore.LoadJson<BilliardFile>("res://data/billiard/billiard.json") ?? new BilliardFile();

    /// <summary>取一组台词（事件名：start / your_turn / win …）；没有就返回空表</summary>
    public static List<CommentLine> Comments(string key) =>
        Data.Comments.TryGetValue(key, out var list) ? list : new List<CommentLine>();
}
