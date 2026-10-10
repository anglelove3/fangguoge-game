using System.Collections.Generic;
using Godot;

/// <summary>
/// 故事时钟：手机状态栏 / 锁屏 / 电脑任务栏统一使用「故事内时间」，
/// 避免真实系统日期与剧情季节、聊天时间戳打架。
/// 数据在 data/story_clock.json；章节没有条目时回退系统时间（如主菜单）。
/// </summary>
public static class StoryClock
{
    private class Entry
    {
        public string Time { get; set; } = "";
        public int Month { get; set; }
        public int Day { get; set; }
        public int Weekday { get; set; }
    }

    private static Dictionary<string, Entry> table;

    private static Dictionary<string, Entry> Table =>
        table ??= DataStore.LoadJson<Dictionary<string, Entry>>("res://data/story_clock.json")
                  ?? new Dictionary<string, Entry>();

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
        return Table.TryGetValue(id, out var e) ? e : null;
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
