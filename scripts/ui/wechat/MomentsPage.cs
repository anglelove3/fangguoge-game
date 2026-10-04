using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 朋友圈页：封面 + 动态流，可点赞、可评论（引擎绘制，数据在 data/moments.json）
///
/// 结构：
///   顶栏（返回 + "朋友圈"）
///   └ 滚动区
///      ├ 封面大图 + 右下角自己的头像/名字（和真朋友圈一样压在封面上）
///      └ 动态列表：头像 / 昵称 / 正文 / 配图 / 时间 / 赞·评论按钮 / 社交灰条 / 评论输入行
/// </summary>
public partial class MomentsPage : Control
{
    /// <summary>点返回（回主框架"发现"页）</summary>
    public event Action BackPressed;

    private static readonly Color NameBlue = new Color(0.34f, 0.42f, 0.58f); // 微信昵称蓝 #576B95
    private static readonly Color TextDark = new Color(0.11f, 0.11f, 0.13f);
    private static readonly Color TextGray = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color SocialBg = new Color(0.965f, 0.965f, 0.97f);

    private const float HeaderHeight = 96f;

    private VBoxContainer feed;
    private MomentsData data;

    /// <summary>一条动态在界面上的句柄（点赞/评论时局部刷新用）</summary>
    private class PostUi
    {
        public MomentPost Post;
        public VBoxContainer SocialBox;   // 赞 + 评论灰条
        public Control InputRow;          // 评论输入行
        public LineEdit Input;
        public Button LikeBtn;
    }

    private readonly List<PostUi> postUis = new();

    // ==================== 搭建 ====================

    public void Build(MomentsData moments)
    {
        data = moments;
        postUis.Clear();
        MouseFilter = MouseFilterEnum.Stop;

        // ---- 顶栏 ----
        var header = new Control { MouseFilter = MouseFilterEnum.Ignore };
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        header.OffsetBottom = HeaderHeight;
        AddChild(header);

        var backBtn = new Button
        {
            Text = "‹",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        backBtn.AddThemeFontSizeOverride("font_size", 44);
        backBtn.AddThemeColorOverride("font_color", TextDark);
        ApplyEmptyStyle(backBtn);
        backBtn.OffsetLeft = 20;
        backBtn.OffsetTop = 10;
        backBtn.OffsetRight = 80;
        backBtn.OffsetBottom = 90;
        backBtn.Pressed += () => BackPressed?.Invoke();
        header.AddChild(backBtn);

        var title = new Label
        {
            Text = "朋友圈",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", TextDark);
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChild(title);

        var headerLine = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.07f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        headerLine.AnchorRight = 1;
        headerLine.OffsetTop = HeaderHeight;
        headerLine.OffsetBottom = HeaderHeight + 2;
        AddChild(headerLine);

        // ---- 滚动区 ----
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AnchorRight = 1;
        scroll.AnchorBottom = 1;
        scroll.OffsetTop = HeaderHeight + 2;
        AddChild(scroll);

        feed = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        feed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        feed.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(feed);

        feed.AddChild(BuildCover());

        foreach (var post in data.Posts)
            feed.AddChild(BuildPost(post));
    }

    /// <summary>封面 + 压角的自己</summary>
    private Control BuildCover()
    {
        var cover = new Control { MouseFilter = MouseFilterEnum.Ignore };
        cover.CustomMinimumSize = new Vector2(0, 340);

        if (!string.IsNullOrEmpty(data.Cover) && ResourceLoader.Exists(data.Cover))
        {
            var tex = new TextureRect
            {
                Texture = GD.Load<Texture2D>(data.Cover),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            tex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            tex.Material = ChatOverlay.MakeMaskMaterial(760, 340, 24);
            cover.AddChild(tex);
        }

        // 自己的头像压在封面右下角（往下探出一点）
        var myAvatar = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/art/chat/mc_avatar_v1.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(112, 112),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        myAvatar.Material = ChatOverlay.MakeMaskMaterial(112, 112, 14);
        myAvatar.AnchorLeft = 1;
        myAvatar.AnchorRight = 1;
        myAvatar.OffsetLeft = -140;
        myAvatar.OffsetTop = 268;
        myAvatar.OffsetRight = -28;
        myAvatar.OffsetBottom = 380;
        cover.AddChild(myAvatar);

        var myName = new Label
        {
            Text = "我",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        myName.AddThemeFontSizeOverride("font_size", 30);
        myName.AddThemeColorOverride("font_color", Colors.White);
        myName.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.45f));
        myName.AddThemeConstantOverride("outline_size", 6);
        myName.AnchorLeft = 0;
        myName.AnchorRight = 1;
        myName.OffsetLeft = 24;
        myName.OffsetTop = 300;
        myName.OffsetRight = -156;
        myName.OffsetBottom = 340;
        cover.AddChild(myName);

        return cover;
    }

    /// <summary>一条动态</summary>
    private Control BuildPost(MomentPost post)
    {
        var ui = new PostUi { Post = post };

        var wrap = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        wrap.AddThemeConstantOverride("separation", 0);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 18);
        wrap.AddChild(UiKit.WrapMargin(row, 24, 24, 24, 0));

        // 头像
        row.AddChild(MakeAuthorAvatar(post, 84));

        // 右侧内容列
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddThemeConstantOverride("separation", 10);
        row.AddChild(col);

        var name = new Label { Text = post.Author, MouseFilter = MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 26);
        name.AddThemeColorOverride("font_color", NameBlue);
        col.AddChild(name);

        if (!string.IsNullOrEmpty(post.Text))
        {
            var text = new Label
            {
                Text = post.Text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            text.AddThemeFontSizeOverride("font_size", 26);
            text.AddThemeColorOverride("font_color", TextDark);
            text.AddThemeConstantOverride("line_spacing", 4);
            col.AddChild(text);
        }

        // 配图（横排，最多 3 张）
        if (post.Images.Count > 0)
        {
            var imgs = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            imgs.AddThemeConstantOverride("separation", 8);
            col.AddChild(imgs);
            int n = Math.Min(post.Images.Count, 3);
            float imgSize = n == 1 ? 340 : 200;
            for (int i = 0; i < n; i++)
            {
                var path = post.Images[i];
                if (!ResourceLoader.Exists(path)) continue;
                var tex = new TextureRect
                {
                    Texture = GD.Load<Texture2D>(path),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    CustomMinimumSize = new Vector2(imgSize, imgSize),
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                tex.Material = ChatOverlay.MakeMaskMaterial(imgSize, imgSize, 8);
                imgs.AddChild(tex);
            }
        }

        // 时间 + 赞/评论按钮
        var meta = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        meta.AddThemeConstantOverride("separation", 14);
        col.AddChild(meta);

        var time = new Label { Text = post.Time, MouseFilter = MouseFilterEnum.Ignore };
        time.AddThemeFontSizeOverride("font_size", 22);
        time.AddThemeColorOverride("font_color", TextGray);
        meta.AddChild(time);

        var expander = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        meta.AddChild(expander);

        ui.LikeBtn = MakeSmallActionBtn(post.Likes.Contains("我") ? "取消赞" : "赞");
        ui.LikeBtn.Pressed += () => ToggleLike(ui);
        meta.AddChild(ui.LikeBtn);

        var commentBtn = MakeSmallActionBtn("评论");
        commentBtn.Pressed += () =>
        {
            ui.InputRow.Visible = !ui.InputRow.Visible;
            if (ui.InputRow.Visible) ui.Input.GrabFocus();
        };
        meta.AddChild(commentBtn);

        // 社交灰条（赞 + 评论）
        ui.SocialBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(ui.SocialBox);
        RebuildSocial(ui);

        // 评论输入行（默认隐藏）
        ui.InputRow = BuildInputRow(ui);
        ui.InputRow.Visible = false;
        col.AddChild(ui.InputRow);

        postUis.Add(ui);

        // 动态之间的细分隔线
        var divider = new ColorRect { Color = new Color(0, 0, 0, 0.05f), MouseFilter = MouseFilterEnum.Ignore };
        divider.CustomMinimumSize = new Vector2(0, 1);
        wrap.AddChild(divider);

        return wrap;
    }

    /// <summary>赞列表 + 评论列表（灰条内容，局部刷新）</summary>
    private void RebuildSocial(PostUi ui)
    {
        foreach (var child in ui.SocialBox.GetChildren())
            child.QueueFree();

        bool hasContent = ui.Post.Likes.Count > 0 || ui.Post.Comments.Count > 0;
        if (!hasContent)
        {
            ui.SocialBox.Visible = false;
            return;
        }
        ui.SocialBox.Visible = true;

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = SocialBg, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        ui.SocialBox.AddChild(panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(UiKit.WrapMargin(box, 14, 10, 14, 10));

        if (ui.Post.Likes.Count > 0)
        {
            var likes = new Label
            {
                Text = "♥ " + string.Join("，", ui.Post.Likes),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            likes.AddThemeFontSizeOverride("font_size", 24);
            likes.AddThemeColorOverride("font_color", NameBlue);
            box.AddChild(likes);
        }

        foreach (var c in ui.Post.Comments)
        {
            var line = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                MouseFilter = MouseFilterEnum.Ignore,
                Text = $"[color=#576B95]{EscapeBbcode(c.Author)}[/color]：{EscapeBbcode(c.Text)}",
            };
            line.AddThemeFontSizeOverride("normal_font_size", 24);
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            box.AddChild(line);
        }
    }

    /// <summary>点赞切换</summary>
    private void ToggleLike(PostUi ui)
    {
        if (ui.Post.Likes.Contains("我"))
            ui.Post.Likes.Remove("我");
        else
            ui.Post.Likes.Add("我");
        ui.LikeBtn.Text = ui.Post.Likes.Contains("我") ? "取消赞" : "赞";
        RebuildSocial(ui);
    }

    /// <summary>评论输入行</summary>
    private Control BuildInputRow(PostUi ui)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);

        ui.Input = new LineEdit
        {
            PlaceholderText = "说点什么……",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 64),
        };
        ui.Input.AddThemeFontSizeOverride("font_size", 24);
        var inputStyle = new StyleBoxFlat
        {
            BgColor = Colors.White,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        };
        inputStyle.SetBorderWidthAll(1);
        inputStyle.BorderColor = new Color(0.85f, 0.85f, 0.87f, 0.6f);
        inputStyle.ContentMarginLeft = 16;
        inputStyle.ContentMarginRight = 16;
        ui.Input.AddThemeStyleboxOverride("normal", inputStyle);
        ui.Input.AddThemeStyleboxOverride("focus", inputStyle);
        row.AddChild(ui.Input);

        var send = new Button
        {
            Text = "发送",
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(96, 64),
        };
        send.AddThemeFontSizeOverride("font_size", 24);
        send.AddThemeColorOverride("font_color", Colors.White);
        send.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.65f, 0.40f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        });
        send.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color(0.34f, 0.70f, 0.44f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        });
        send.AddThemeStyleboxOverride("pressed", new StyleBoxFlat
        {
            BgColor = new Color(0.24f, 0.55f, 0.34f),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        });
        send.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        row.AddChild(send);

        void Submit()
        {
            string t = ui.Input.Text.Trim();
            if (t.Length == 0) return;
            ui.Post.Comments.Add(new MomentComment { Author = "我", Text = t });
            ui.Input.Text = "";
            ui.InputRow.Visible = false;
            RebuildSocial(ui);
        }
        send.Pressed += Submit;
        ui.Input.TextSubmitted += _ => Submit();

        return row;
    }

    // ==================== 小工具 ====================

    /// <summary>动态作者头像：有图用图，没图首字色块</summary>
    private static Control MakeAuthorAvatar(MomentPost post, float size)
    {
        if (!string.IsNullOrEmpty(post.Avatar) && ResourceLoader.Exists(post.Avatar))
        {
            var tex = new TextureRect
            {
                Texture = GD.Load<Texture2D>(post.Avatar),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = SizeFlags.ShrinkBegin, // 顶对齐，别被动态高度拉成竖条
                MouseFilter = MouseFilterEnum.Ignore,
            };
            tex.Material = ChatOverlay.MakeMaskMaterial(size, size, 12);
            return tex;
        }
        var initial = new InitialAvatar(post.Author, size);
        initial.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        return initial;
    }

    private static Button MakeSmallActionBtn(string text)
    {
        var btn = new Button
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.AddThemeFontSizeOverride("font_size", 22);
        btn.AddThemeColorOverride("font_color", NameBlue);
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
        return btn;
    }

    private static void ApplyEmptyStyle(Button btn)
    {
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
    }

    /// <summary>防止文案里的方括号被当成 BBCode</summary>
    private static string EscapeBbcode(string s)
    {
        return s.Replace("[", "[lb]").Replace("]", "[rb]");
    }
}
