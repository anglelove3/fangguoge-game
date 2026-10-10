using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 微信主框架 / 朋友圈的数据模型 + JSON 加载
///
/// 和聊天系统同一套流派：内容全放 JSON，改文案不碰代码。
///   data/contacts.json  → 联系人 + 每人的占位聊天
///   data/moments.json   → 朋友圈动态（含预置点赞/评论）
/// </summary>
public static class WeChatData
{
    /// <summary>男主自己的微信头像（海绵宝宝）</summary>
    public const string MyAvatarPath = "res://assets/art/chat/avatar_spongebob_v1.png";

    /// <summary>宝宝（前女友）的微信头像（夕阳下的她，大学的记忆）</summary>
    public const string BaobaoAvatarPath = "res://assets/art/chat/her_avatar_v1.png";

    private static ContactsData contactsRaw;
    private static MomentsData momentsRaw;

    /// <summary>
    /// 联系人列表：原始表只读一次，每次按当前章节过滤后再交付。
    /// （afterChapter 没到的联系人先不出现——比如「今天吃什么研讨组」，
    ///   玩家进到第五章，这群朋友才会出现在会话列表里。）
    /// 注意：只换列表容器，联系人对象还是同一批引用，红点/新消息照样共用。
    /// </summary>
    public static ContactsData LoadContacts()
    {
        contactsRaw ??= Load<ContactsData>("res://data/contacts.json") ?? new ContactsData();

        int chapter = GameManager.Instance?.CurrentChapter ?? 0;
        var visible = new ContactsData { Contacts = new List<ContactData>() };
        foreach (var c in contactsRaw.Contacts)
        {
            if (c.AfterChapter <= chapter)
                visible.Contacts.Add(c);
        }
        return visible;
    }

    /// <summary>
    /// 朋友圈动态：原始表只读一次，每次按当前章节过滤后再交付。
    /// （afterChapter 没到的动态先不出现——朋友圈也随时间往前走。）
    /// 注意：只换列表容器，动态对象还是同一批引用，点赞/评论的改动照样保留。
    /// </summary>
    public static MomentsData LoadMoments()
    {
        momentsRaw ??= Load<MomentsData>("res://data/moments.json") ?? new MomentsData();

        int chapter = GameManager.Instance?.CurrentChapter ?? 0;
        var visible = new MomentsData { Cover = momentsRaw.Cover, Posts = new List<MomentPost>() };
        foreach (var p in momentsRaw.Posts)
        {
            if (p.AfterChapter <= chapter)
                visible.Posts.Add(p);
        }
        return visible;
    }

    private static SystemWechatData systemCache;

    /// <summary>订阅号文章 / 微信运动排行等"系统账号"数据</summary>
    public static SystemWechatData LoadSystem()
    {
        if (systemCache != null) return systemCache;
        systemCache = Load<SystemWechatData>("res://data/system_wechat.json") ?? new SystemWechatData();
        return systemCache;
    }

    /// <summary>按昵称找联系人（群聊消息给发言人配头像用）；找不到返回 null</summary>
    public static ContactData FindByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return LoadContacts().Contacts.Find(c => c.Name == name);
    }

    private static T Load<T>(string path) where T : class
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[微信] 找不到数据文件：{path}");
            return null;
        }
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(FileAccess.GetFileAsString(path), options);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[微信] JSON 解析失败 {path}：{e.Message}");
            return null;
        }
    }
}

/// <summary>联系人列表文件</summary>
public class ContactsData
{
    public List<ContactData> Contacts { get; set; } = new();
}

/// <summary>一位联系人（附带 ta 的占位聊天记录）；也承载群聊和系统账号条目</summary>
public class ContactData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "";   // 头像图路径；留空 = 用名字首字生成色块头像
    public string ChatFile { get; set; } = ""; // 非空时聊天内容读 data/chat/{ChatFile}.json（同桌走这条）
    public string SessionTime { get; set; } = DataStore.Text("wx.session_time_default");
    public List<ChatMessageData> Messages { get; set; } = new();

    // ---------- 第九轮：群聊 / 系统账号 ----------

    /// <summary>"group" = 群聊（气泡上方显示发言人昵称）；留空 = 普通联系人</summary>
    public string Kind { get; set; } = "";

    /// <summary>群成员数（顶栏显示"群名（N）"）</summary>
    public int Members { get; set; }

    /// <summary>只读会话：输入框禁言（群聊围观 / 系统消息都用它）</summary>
    public bool ReadOnly { get; set; }

    /// <summary>会话列表预览文字；留空 = 自动取最后一条消息</summary>
    public string PreviewText { get; set; } = "";

    /// <summary>非空 = 点击不打开聊天，只弹这条 toast（服务号/微信支付这类纯装饰行）</summary>
    public string Toast { get; set; } = "";

    /// <summary>系统账号图标字形（白字色块，如 "订""步""¥"）；非空时优先于 Avatar</summary>
    public string Icon { get; set; } = "";

    /// <summary>系统账号图标底色，如 "#2B7CFF"；留空默认蓝色</summary>
    public string IconColor { get; set; } = "";

    /// <summary>头像右上角红点角标文字（如 "2"）；留空 = 无角标</summary>
    public string Badge { get; set; } = "";

    /// <summary>群头像拼格：取前 4 位成员的头像路径画 2×2 九宫格</summary>
    public List<string> GroupAvatars { get; set; } = new();

    /// <summary>
    /// 这位联系人从第几章开始出现（0 = 一开始就在；5 = 玩家进到第五章才认识这群朋友）。
    /// 用来让联系人列表跟着剧情走——人不是凭空冒出来的。
    /// </summary>
    public int AfterChapter { get; set; }

    /// <summary>点开的页面："subscriptions" = 订阅号文章列表，"steps" = 微信运动排行；留空 = 聊天页</summary>
    public string OpenPage { get; set; } = "";

    /// <summary>会话列表里的"最后一条消息"预览</summary>
    public string Preview
    {
        get
        {
            if (!string.IsNullOrEmpty(PreviewText)) return PreviewText;
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].Type != "divider" && Messages[i].Type != "image")
                    return Messages[i].Text;
            }
            return DataStore.Text("wx.image_preview");
        }
    }
}

/// <summary>朋友圈文件</summary>
public class MomentsData
{
    public string Cover { get; set; } = "";
    public List<MomentPost> Posts { get; set; } = new();
}

/// <summary>一条朋友圈动态</summary>
public class MomentPost
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Time { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Images { get; set; } = new();
    public List<string> Likes { get; set; } = new();
    public List<MomentComment> Comments { get; set; } = new();

    /// <summary>
    /// 玩家评论后的延迟回复规则：按评论里的关键词挑最贴的一组回复（混合模式触发，
    /// 手机开着 15~40 秒后浮出，否则挂到下次打开朋友圈）。
    /// </summary>
    public List<CommentReplyRule> CommentReplies { get; set; } = new();

    /// <summary>评论没命中任何关键词时的兜底回复池</summary>
    public List<string> CommentFallback { get; set; } = new();

    /// <summary>
    /// 这条动态从第几章开始出现（0 = 一开始就在；3 = 玩家进到第三章才刷出来）。
    /// 用来让朋友圈跟着剧情走：人还是那些人，时间线却在动。
    /// </summary>
    public int AfterChapter { get; set; }
}

/// <summary>朋友圈评论回复规则：评论命中 keywords 之一 → 从 lines 里抽一条回</summary>
public class CommentReplyRule
{
    public List<string> Keywords { get; set; } = new();
    public List<string> Lines { get; set; } = new();
}

/// <summary>朋友圈评论</summary>
public class MomentComment
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}

// ==================== 系统账号数据（第九轮） ====================

/// <summary>data/system_wechat.json：订阅号文章列表 + 微信运动排行</summary>
public class SystemWechatData
{
    public List<SubscriptionArticle> Articles { get; set; } = new();
    public List<StepEntry> Steps { get; set; } = new();

    /// <summary>微信运动顶部卡片文案（男主自己的步数感悟）</summary>
    public string MySteps { get; set; } = "385";
    public string MyStepsCaption { get; set; } = "";
}

/// <summary>一条订阅号文章（列表行；点击只弹 toast，正文是摆设）</summary>
public class SubscriptionArticle
{
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";  // 公众号名
    public string Time { get; set; } = "";
    public string Reads { get; set; } = "";   // "10万+"
}

/// <summary>微信运动排行的一行</summary>
public class StepEntry
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string Remark { get; set; } = "";  // 点赞小字（可选）
}
