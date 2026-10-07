using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 联系人"人设回复引擎"（本地关键词版，不联网、不花任何费用）
///
/// 数据在 data/chat/replies.json：
///   topics   → 全局话题关键词表（顺序即优先级，靠前的先判）
///   contacts → 每位联系人按人设预写的回复：
///     delay    回复延迟区间（秒，按性格不同：金艮秒回、导师隔半天）
///     greet/food/... 各话题的人设回复池（每个话题 4~6 条）
///     fallback 兜底闲聊池（消息没命中任何话题时用）
///     nudge    玩家连发 3 条还没等到回复时的催促彩蛋
///     grudge   被玩家"损"够 2 次后的记仇反呛彩蛋
///     warm/cold 回复温度前导词池（暖：她刚说完话你就回 / 冷：隔了很久你才回）
///
/// 玩家消息 → 按关键词命中数归入一个话题 → 从对应回复池抽一条（同一池子抽过的先不重复，
/// 抽完一轮才重置）。回复只图热闹，不加好感/勇气——数值仍由剧情选择控制，防止刷分。
///
/// 文案全在 JSON 里，想改语气/加话，编辑 data/chat/replies.json 即可，不用碰代码。
/// </summary>
public static class ReplyEngine
{
    private class TopicRule
    {
        public string Id { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
    }

    private class ContactProfile
    {
        public List<double> Delay { get; set; } = new();
        public Dictionary<string, List<string>> Topics { get; set; } = new();
        public List<string> Fallback { get; set; } = new();
        public List<string> Nudge { get; set; } = new();
        public List<string> Grudge { get; set; } = new();
        public List<string> Warm { get; set; } = new(); // 暖前导词池（她刚说完话你就回）
        public List<string> Cold { get; set; } = new(); // 冷前导词池（隔了很久你才回）
    }

    private class ReplyData
    {
        public List<TopicRule> Topics { get; set; } = new();
        public List<double> DefaultDelay { get; set; } = new();
        public Dictionary<string, ContactProfile> Contacts { get; set; } = new();
    }

    /// <summary>一次回复的完整决定：说什么、什么性质、隔多久</summary>
    public enum ReplyKind { Normal, Nudge, Grudge }

    public sealed class Decision
    {
        public string Line;
        public ReplyKind Kind;
        public float Delay;

        /// <summary>回复温度的前导词（暖/冷才有；空 = 直接进主回复）</summary>
        public string PreLine = "";

        /// <summary>前导词延迟（相对玩家消息的秒数；PreLine 为空时无意义）</summary>
        public float PreDelay;
    }

    // ==================== 回复温度（第十二轮：回复快慢有温度） ====================
    // 温度 =「她上次说话 → 你现在回她」的新鲜度，只在"新一轮对话的第一句"上演：
    //   暖：她刚说过话（≤30 秒；含"有未读时点开会话"这一刻）→ 她应得快、先应一声
    //   冷：她上一条已过去 ≥75 秒，你才想起回 → 她慢半拍、先冷淡一句
    // "新一轮的第一句" = 距我上次发言 ≥40 秒（连续对聊中不重复演温度，防刷戏）。
    // 温度只改"回法"（前导词 + 回复延迟），绝不动好感/勇气——数值仍由剧情选择控制，防刷分。

    private const double WarmGap = 30.0;  // 她说完 30 秒内回她 = 暖
    private const double ColdGap = 75.0;  // 她说完 75 秒后才回 = 冷
    private const double NewSessionGap = 40.0; // 距我上次发言多久算"新的一轮"

    /// <summary>她（联系人）最近一次说话的时刻（秒，单调时钟）</summary>
    private static readonly Dictionary<string, double> lastMsgAt = new();

    /// <summary>我最近一次发言的时刻（秒，单调时钟）</summary>
    private static readonly Dictionary<string, double> lastPlayerAt = new();

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>她发来一条消息 → 记录对话热度（ChatOverlay.StoreIncoming 统一调用）</summary>
    public static void NoteContactMessage(string contactId)
    {
        if (!string.IsNullOrEmpty(contactId))
            lastMsgAt[contactId] = Now;
    }

    /// <summary>玩家打开会话：有未读说明她刚说的话"正被看到"，气儿还是热的</summary>
    public static void NoteChatOpened(string contactId, bool hadUnread)
    {
        if (hadUnread && !string.IsNullOrEmpty(contactId))
            lastMsgAt[contactId] = Now;
    }

    /// <summary>我发了一条消息（自由输入走引擎时自动记；脚本预设回复由聊天界面代记）</summary>
    public static void NotePlayerMessage(string contactId)
    {
        if (!string.IsNullOrEmpty(contactId))
            lastPlayerAt[contactId] = Now;
    }

    private static ReplyData data;
    private static readonly Random rng = new();

    /// <summary>每个池子的"已用过"标记（联系人+池名），用完一轮才重置，避免连续重复</summary>
    private static readonly Dictionary<string, HashSet<string>> usedLines = new();

    /// <summary>各联系人的"被损"计数（记仇彩蛋用）</summary>
    private static readonly Dictionary<string, int> teaseCounts = new();

    /// <summary>各联系人"发了消息还没等到回复"的条数（催促彩蛋用）</summary>
    private static readonly Dictionary<string, int> sentSinceReply = new();

    private static ReplyData GetData()
    {
        if (data != null)
            return data;
        const string path = "res://data/chat/replies.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[回复引擎] 找不到数据文件：{path}");
            return null;
        }
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
            data = JsonSerializer.Deserialize<ReplyData>(FileAccess.GetFileAsString(path), options);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[回复引擎] JSON 解析失败 {path}：{e.Message}");
        }
        return data;
    }

    /// <summary>某联系人是否配了人设回复</summary>
    public static bool HasProfile(string contactId)
    {
        var d = GetData();
        return d?.Contacts.ContainsKey(contactId) == true;
    }

    /// <summary>玩家给某联系人发了一条消息 → 返回回复决定（没配人设返回 null）</summary>
    public static Decision OnPlayerMessage(string contactId, string message)
    {
        var d = GetData();
        if (d == null || !d.Contacts.TryGetValue(contactId, out var profile))
            return null;

        // 1. 连发催促：等不到回复又连发 3 条以上，先催一句
        sentSinceReply.TryGetValue(contactId, out int burst);
        burst++;
        sentSinceReply[contactId] = burst;

        // 记仇优先级高于催促：玩家明明在"损人"，该反呛就反呛，别被催促抢了戏
        string topic = Classify(d, message);

        bool isTease = topic == "tease";
        int teaseCount = 0;
        if (isTease)
        {
            teaseCounts.TryGetValue(contactId, out teaseCount);
            teaseCount++;
            teaseCounts[contactId] = teaseCount;
        }

        ReplyKind kind = ReplyKind.Normal;
        string poolKey;
        List<string> pool;

        if (isTease && teaseCount >= 2 && profile.Grudge is { Count: > 0 })
        {
            // 2. 记仇反呛：被"损"满 2 次，触发一次记仇彩蛋
            kind = ReplyKind.Grudge;
            poolKey = "grudge";
            pool = profile.Grudge;
            teaseCounts[contactId] = 0;
            sentSinceReply[contactId] = 0;
        }
        else if (burst >= 3 && profile.Nudge is { Count: > 0 })
        {
            // 1. 连发催促：等不到回复又连发 3 条以上，先催一句
            kind = ReplyKind.Nudge;
            poolKey = "nudge";
            pool = profile.Nudge;
            sentSinceReply[contactId] = 0; // 催完这轮重新数
        }
        else
        {
            if (isTease)
            {
                poolKey = "tease";
                profile.Topics.TryGetValue("tease", out pool);
                pool ??= profile.Fallback;
            }
            else if (profile.Topics.TryGetValue(topic, out var tp) && tp is { Count: > 0 })
            {
                poolKey = topic;
                pool = tp;
            }
            else
            {
                poolKey = "fallback";
                pool = profile.Fallback;
            }

            // 兜底池也空的话，随便回一句通用的
            if (pool is not { Count: > 0 })
                pool = new List<string> { "嗯" };
        }

        string line = PickLine(contactId, poolKey, pool);
        if (line == null)
            return null;

        float delay = PickDelay(d, profile);
        string preLine = "";
        float preDelay = 0f;

        // 回复温度：只挂在普通回复上（催促/记仇是独立彩蛋，不掺温度）
        double now = Now;
        bool newSession = !lastPlayerAt.TryGetValue(contactId, out double prev) || now - prev >= NewSessionGap;
        lastPlayerAt[contactId] = now;
        if (kind == ReplyKind.Normal && newSession && lastMsgAt.TryGetValue(contactId, out double lastMsg))
        {
            double gap = now - lastMsg;
            if (gap <= WarmGap && profile.Warm is { Count: > 0 })
            {
                preLine = PickLine(contactId, "warm", profile.Warm);
                preDelay = 0.8f;
                delay = Mathf.Clamp(delay * 0.45f, 0.8f, 3.0f);
            }
            else if (gap >= ColdGap && profile.Cold is { Count: > 0 })
            {
                preLine = PickLine(contactId, "cold", profile.Cold);
                preDelay = 1.8f;
                delay = Mathf.Clamp(delay * 1.6f, 4.5f, 10f);
            }
        }
        if (preLine != "")
            delay = Mathf.Max(delay, preDelay + 1.2f); // 主回复永远排在前导词后面

        return new Decision
        {
            Line = line,
            Kind = kind,
            Delay = delay,
            PreLine = preLine,
            PreDelay = preDelay,
        };
    }

    /// <summary>关键词命中数最多的话题获胜；没命中返回 greet（打招呼类兜底）之外的话题名 —— 这里直接返回 "" 由调用方走 fallback</summary>
    private static string Classify(ReplyData d, string message)
    {
        string best = "";
        int bestHits = 0;
        foreach (var rule in d.Topics)
        {
            int hits = 0;
            foreach (var kw in rule.Keywords)
            {
                if (string.IsNullOrEmpty(kw))
                    continue;
                if (message.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    hits++;
            }
            // 严格大于：并列时保持 topics 里的优先顺序（tease/sorry 排最前）
            if (hits > bestHits)
            {
                bestHits = hits;
                best = rule.Id;
            }
        }
        return best;
    }

    /// <summary>从一个池子里抽一条没抽过的（全抽过就重置这个池子）</summary>
    private static string PickLine(string contactId, string poolKey, List<string> pool)
    {
        string key = $"{contactId}/{poolKey}";
        if (!usedLines.TryGetValue(key, out var used))
        {
            used = new HashSet<string>();
            usedLines[key] = used;
        }

        var remaining = new List<string>();
        foreach (var line in pool)
            if (!used.Contains(line))
                remaining.Add(line);
        if (remaining.Count == 0)
        {
            used.Clear();
            remaining.AddRange(pool);
        }

        var pick = remaining[rng.Next(remaining.Count)];
        used.Add(pick);
        return pick;
    }

    private static float PickDelay(ReplyData d, ContactProfile profile)
    {
        var range = profile.Delay is { Count: > 0 } ? profile.Delay : d.DefaultDelay;
        if (range is not { Count: > 0 })
            return (float)GD.RandRange(2.5, 5.0);
        float lo = (float)range[0];
        float hi = range.Count > 1 ? (float)range[1] : lo;
        if (hi < lo) (lo, hi) = (hi, lo);
        return (float)GD.RandRange(lo, hi);
    }
}
