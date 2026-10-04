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
    /// <summary>男主自己的微信头像（和宝宝的情侣头像：我是海绵宝宝）</summary>
    public const string MyAvatarPath = "res://assets/art/chat/avatar_spongebob_v1.png";

    /// <summary>宝宝（前女友）的微信头像（情侣头像的另一半：她是派大星）</summary>
    public const string BaobaoAvatarPath = "res://assets/art/chat/avatar_patrick_v1.png";

    private static ContactsData contactsCache;
    private static MomentsData momentsCache;

    public static ContactsData LoadContacts()
    {
        if (contactsCache != null) return contactsCache;
        contactsCache = Load<ContactsData>("res://data/contacts.json") ?? new ContactsData();
        return contactsCache;
    }

    public static MomentsData LoadMoments()
    {
        if (momentsCache != null) return momentsCache;
        momentsCache = Load<MomentsData>("res://data/moments.json") ?? new MomentsData();
        return momentsCache;
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

/// <summary>一位联系人（附带 ta 的占位聊天记录）</summary>
public class ContactData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "";   // 头像图路径；留空 = 用名字首字生成色块头像
    public string ChatFile { get; set; } = ""; // 非空时聊天内容读 data/chat/{ChatFile}.json（同桌走这条）
    public string SessionTime { get; set; } = "昨天";
    public List<ChatMessageData> Messages { get; set; } = new();

    /// <summary>会话列表里的"最后一条消息"预览</summary>
    public string Preview
    {
        get
        {
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].Type != "divider" && Messages[i].Type != "image")
                    return Messages[i].Text;
            }
            return "[图片]";
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
}

/// <summary>朋友圈评论</summary>
public class MomentComment
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}
