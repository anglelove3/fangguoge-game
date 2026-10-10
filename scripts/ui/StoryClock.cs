using System.Collections.Generic;
using Godot;

/// <summary>
/// 故事时钟：手机状态栏 / 锁屏 / 电脑任务栏统一使用「故事内时间」，
/// 避免真实系统日期与剧情季节、聊天时间戳打架。
/// 数据在 data/story_clock.json；章节没有条目时回退系统时间（如主菜单）。
///
/// 【第十七轮新增：章内时段】
/// 一章不一定只发生在一个时刻。第八章跨了六天（周一宿舍 → 周二实验室 →
/// 周六婚宴 → 周日老校门），要是手机一直显示"10月27日"，玩家会在婚礼当天看见错的日期。
/// 所以一章下面可以挂若干 segments（时段），章节脚本进每一幕时点名要用哪一段：
///   StoryClock.UseSegment("wedding");
/// 出了这一章（切场景 / 回菜单）自动作废，不用怕忘了清。
/// </summary>
public static class StoryClock
{
    private class Entry
    {
        public string Time { get; set; } = "";
        public int Month { get; set; }
        public int Day { get; set; }
        public int Weekday { get; set; }

        /// <summary>章内时段：键是时段名（wedding / gate…），值长得和章节条目一样</summary>
        public Dictionary<string, Entry> Segments { get; set; }
    }

    private static Dictionary<string, Entry> table;

    private static Dictionary<string, Entry> Table =>
        table ??= DataStore.LoadJson<Dictionary<string, Entry>>("res://data/story_clock.json")
                  ?? new Dictionary<string, Entry>();

    // ---------- 章内时段 ----------

    private static string segmentId;
    private static string segmentChapter;

    /// <summary>当前生效的时段名（没点名 = 空串）</summary>
    public static string Segment => segmentId ?? "";

    /// <summary>本章这一幕用哪个时段（名字写错不报错，只会安静地退回章节主时刻——所以拼写自己核对）</summary>
    public static void UseSegment(string id)
    {
        segmentId = id;
        segmentChapter = DialogueManager.Instance?.CurrentChapterId ?? "";
    }

    /// <summary>清掉时段（章节脚本 _ExitTree 调一次；下一章的 UseSegment 也会自然覆盖）</summary>
    public static void ClearSegment()
    {
        segmentId = null;
        segmentChapter = null;
    }

    /// <summary>章节顺序：prologue=0，ch01=1……用于 gating 判断</summary>
    public static int Order(string chapterId)
    {
        if (string.IsNullOrEmpty(chapterId))
            return -1;
        if (chapterId == "prologue")
            return 0;
        if (chapterId.StartsWith("ch") && int.TryParse(chapterId.Substring(2), out int n))
            return n;
        return -1;
    }

    private static Entry Current()
    {
        var id = DialogueManager.Instance?.CurrentChapterId ?? "";
        if (!Table.TryGetValue(id, out var e))
            return null;

        // 章内时段：只有"点名时所在的章"和"现在所在的章"是同一章才生效，
        // 免得第八章清干净之前，第九章的手机还穿着婚宴的时间。
        if (!string.IsNullOrEmpty(segmentId) && segmentChapter == id
            && e.Segments != null && e.Segments.TryGetValue(segmentId, out var seg))
            return seg;

        return e;
    }

    /// <summary>HH:mm；无故事条目时回退系统时间</summary>
    public static string ClockText()
    {
        var e = Current();
        if (e != null && !string.IsNullOrEmpty(e.Time))
            return e.Time;
        var t = Time.GetTimeDictFromSystem();
        return $"{t["hour"].AsInt32():D2}:{t["minute"].AsInt32():D2}";
    }

    /// <summary>「M月D日 星期X」；无故事条目时回退系统日期</summary>
    public static string DateText()
    {
        var e = Current();
        if (e != null && e.Month > 0 && e.Day > 0)
            return $"{e.Month}月{e.Day}日 {WeekCn(e.Weekday)}";
        var d = Time.GetDateDictFromSystem();
        return $"{d["month"].AsInt32()}月{d["day"].AsInt32()}日 {WeekCn(d["weekday"].AsInt32())}";
    }

    /// <summary>
    /// 微信式时间戳（"上午 10:26" / "晚上 10:07"）。
    /// 聊天里新冒出来的消息用它盖章——以前是从一张演示时刻表里随机抽一个，
    /// 结果第八章上午在实验室回消息，屏幕上写着"晚上 10:15"，比剧情还早一个白天。
    ///
    /// 分档按中文口语来：上午到 12 点整（第八章台词会说"这个我上午就想好了"，
    /// 11:35 打成"中午"就跟自己吵架），中午只留 12~14 点这一小段。
    /// </summary>
    public static string StampText()
    {
        var t = ClockText();
        var parts = t.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute))
            return t;

        string period = hour switch
        {
            < 5 => "凌晨",
            < 9 => "早上",
            < 12 => "上午",
            < 14 => "中午",
            < 18 => "下午",
            _ => "晚上",
        };
        int h12 = hour % 12;
        if (h12 == 0)
            h12 = 12;
        return $"{period} {h12}:{minute:D2}";
    }

    /// <summary>当前章节是否已到达 after（含该章）；after 为空恒为真</summary>
    public static bool Reached(string after)
    {
        if (string.IsNullOrEmpty(after))
            return true;
        return Order(DialogueManager.Instance?.CurrentChapterId ?? "") >= Order(after);
    }

    private static string WeekCn(int weekday) => weekday switch
    {
        0 => "星期日",
        1 => "星期一",
        2 => "星期二",
        3 => "星期三",
        4 => "星期四",
        5 => "星期五",
        _ => "星期六",
    };
}
