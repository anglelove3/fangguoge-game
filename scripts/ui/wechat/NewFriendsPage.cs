using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 通讯录 →「新的朋友」页（第十八轮）：等着玩家按下"添加到通讯录"的名片。
///
/// 【谁出现在这里】完全由 data/contacts.json 决定：
///   requiresFriend = true、afterChapter 已经到了、存档里还没加过 → 就是待添加。
/// 代码里一个人名都不写——以后要加新朋友，改 JSON 加一条联系人就够了。
///
/// 【按下去会发生什么】GameManager.AddFriend 落进存档（关系账），
/// 顺手给 ta 的会话挂红点（unreadOnAdd），并通知外面重建会话列表/通讯录。
/// 加完之后这一行变成灰色的"已添加"，下次再进来整条都不出现了。
/// </summary>
public partial class NewFriendsPage : Control
{
    /// <summary>点了返回</summary>
    public event Action BackPressed;
    /// <summary>要点一条 toast（参数 = 文案）</summary>
    public event Action<string> ToastRequested;
    /// <summary>刚把某位加进了通讯录（参数 = 联系人 id）</summary>
    public event Action<string> FriendAdded;

    private static readonly Color PageBg = new Color(0.935f, 0.935f, 0.94f);
    private static readonly Color TextDark = new Color(0.11f, 0.11f, 0.13f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color Green = new Color(0.16f, 0.62f, 0.30f);

    private VBoxContainer box;

    public void Build()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(SystemPagesKit.MakePageBg(PageBg));

        var header = SystemPagesKit.BuildHeader(DataStore.Text("sys.new_friends_title"), () => BackPressed?.Invoke());
        AddChild(header);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 102;
        AddChild(scroll);

        box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(box);

        Render();
    }

    /// <summary>加过朋友之后重画一遍（红点那条列表项要不要留、空状态，都在这一步体现）</summary>
    public void Refresh()
    {
        if (box != null && GodotObject.IsInstanceValid(box))
            Render();
    }

    private void Render()
    {
        foreach (var child in box.GetChildren())
            child.QueueFree();

        var pending = WeChatData.PendingFriends();
        if (pending.Count == 0)
        {
            var empty = SystemPagesKit.MakeLabel(DataStore.Text("sys.new_friends_empty"), 26, TextGray);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(UiKit.WrapMargin(empty, 28, 60, 28, 60));
            return;
        }

        var tip = SystemPagesKit.MakeLabel(DataStore.Text("sys.new_friends_tip"), 22, TextGray);
        box.AddChild(UiKit.WrapMargin(tip, 28, 18, 28, 12));

        foreach (var c in pending)
            box.AddChild(MakeRequestRow(c));
    }

    /// <summary>一张待添加的名片：头像 + 名字 + 来源 + 「添加到通讯录」按钮</summary>
    private Control MakeRequestRow(ContactData c)
    {
        var row = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = Colors.White };
        style.BorderWidthBottom = 1;
        style.BorderColor = new Color(0, 0, 0, 0.05f);
        style.ContentMarginLeft = 28;
        style.ContentMarginRight = 28;
        row.AddThemeStyleboxOverride("panel", style);
        row.CustomMinimumSize = new Vector2(0, 132);

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 20);
        row.AddChild(hbox);

        var avatar = WeChatMainPage.MakeAvatar(c, 92);
        avatar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        hbox.AddChild(avatar);

        var mid = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        mid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mid.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        mid.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(mid);
        mid.AddChild(SystemPagesKit.MakeLabel(c.Name, 30, TextDark));
        if (!string.IsNullOrEmpty(c.FriendSource))
            mid.AddChild(SystemPagesKit.MakeLabel(c.FriendSource, 22, TextGray));

        var add = new Button
        {
            Text = DataStore.Text("sys.new_friends_add"),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        add.AddThemeFontSizeOverride("font_size", 24);
        add.AddThemeColorOverride("font_color", Colors.White);
        var btnStyle = new StyleBoxFlat { BgColor = Green };
        btnStyle.SetCornerRadiusAll(10);
        btnStyle.ContentMarginLeft = 20;
        btnStyle.ContentMarginRight = 20;
        btnStyle.ContentMarginTop = 10;
        btnStyle.ContentMarginBottom = 10;
        add.AddThemeStyleboxOverride("normal", btnStyle);
        add.AddThemeStyleboxOverride("hover", btnStyle);
        add.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color(0.12f, 0.5f, 0.24f), });
        add.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        hbox.AddChild(add);

        add.Pressed += () => Accept(c, add);
        return row;
    }

    /// <summary>按下"添加到通讯录"：落存档 → 挂红点 → 这行变"已添加" → 通知外面刷新列表</summary>
    private void Accept(ContactData c, Button add)
    {
        GameManager.Instance?.AddFriend(c.Id, c.FriendEvent);
        if (c.UnreadOnAdd > 0)
            LiveEvents.GiveUnread(c.Id, c.UnreadOnAdd);

        add.Text = DataStore.Text("sys.new_friends_added");
        add.Disabled = true;
        var done = new StyleBoxFlat { BgColor = new Color(0.72f, 0.74f, 0.76f) };
        done.SetCornerRadiusAll(10);
        done.ContentMarginLeft = 20;
        done.ContentMarginRight = 20;
        done.ContentMarginTop = 10;
        done.ContentMarginBottom = 10;
        add.AddThemeStyleboxOverride("normal", done);
        add.AddThemeStyleboxOverride("hover", done);
        add.AddThemeStyleboxOverride("disabled", done);

        ToastRequested?.Invoke(DataStore.Text("sys.new_friends_toast", c.Name));
        FriendAdded?.Invoke(c.Id);
        AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft);
    }
}
