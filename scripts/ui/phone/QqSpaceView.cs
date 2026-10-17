using Godot;
using System.Collections.Generic;

/// <summary>
/// QQ 空间（第三轮：出轨线的「物证发现」载体）
///
/// 手机桌面多了一个 QQ 图标。他的前女友不常用微信朋友圈，但 QQ 空间还开着。
/// 三条动态——前两条看起来都正常，第三条是一张照片：
///
///   酒店镜子前，她笑得很开心。旁边有个男人的手，搭在她肩上。
///   日期是他们「还在一起」的时候。
///
/// 点开照片：画面缓慢放大，声音一样一样消失——BGM 停、只剩自己的心跳，
/// 然后黑屏，一行字。这一刻没有任何 CG、没有争吵，伤害是他自己翻出来的。
/// </summary>
public partial class QqSpaceView : PhoneAppPage
{
    private VBoxContainer feed;
    private Control photoOverlay;
    private Texture2D mirrorPhoto;
    private bool revealDone;

    /// <summary>发现演出结束（剧情用它推进）</summary>
    public event System.Action Discovered;

    /// <summary>本局是否已经「看见」过那张照片（跨章节静态标记）</summary>
    public static bool DiscoveredOnce;

    private class Moment
    {
        public string Time = "";
        public string Text = "";
        public string Photo = "";   // 空 = 纯文字
        public string PhotoHint = "";
    }

    private static readonly Moment[] Moments =
    {
        new Moment
        {
            Time = "3月2日 · 来自 iPhone",
            Text = "和静静逛吃的一天～这家舒芙蕾真的绝了 [图片]",
        },
        new Moment
        {
            Time = "3月15日 · 来自 iPhone",
            Text = "加班到十点，谁懂啊……求安慰 [苦涩]",
            PhotoHint = "",
        },
        new Moment
        {
            Time = "3月18日 · 来自 iPhone",
            Text = "周末愉快～",
            Photo = "res://assets/art/props/hotel_mirror_photo.png",
            PhotoHint = "（照片。点开看。）",
        },
    };

    public override void _Ready()
    {
        // QQ 蓝头栏
        var (_, box, _) = MakeHeader(new Color(0.07f, 0.72f, 0.96f));
        AddBackAndTitle(box, "QQ空间", new Color(1f, 1f, 1f));
        BuildFeed();
    }

    private void BuildFeed()
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetTop = 112;
        scroll.OffsetBottom = -12;
        scroll.OffsetLeft = 10;
        scroll.OffsetRight = -10;
        AddChild(scroll);

        feed = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        feed.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(feed);

        foreach (var m in Moments)
            feed.AddChild(MakeMomentCard(m));

        // 底部留白
        feed.AddChild(new Control { CustomMinimumSize = new Vector2(0, 40), MouseFilter = MouseFilterEnum.Ignore });
    }

    private Control MakeMomentCard(Moment m)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 1f) };
        style.SetCornerRadiusAll(10);
        style.ContentMarginLeft = 16; style.ContentMarginRight = 16;
        style.ContentMarginTop = 14; style.ContentMarginBottom = 14;
        card.AddThemeStyleboxOverride("panel", style);

        var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 8);
        card.AddChild(col);

        // 作者行：她的 QQ 昵称 + 小头像色块
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10);
        var avatar = new PanelContainer { CustomMinimumSize = new Vector2(40, 40) };
        var avStyle = new StyleBoxFlat { BgColor = new Color(0.94f, 0.72f, 0.68f) };
        avStyle.SetCornerRadiusAll(20);
        avatar.AddThemeStyleboxOverride("panel", avStyle);
        var avLabel = new Label { Text = "宝", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        avLabel.AddThemeFontSizeOverride("font_size", 20);
        avLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        avatar.AddChild(avLabel);
        head.AddChild(avatar);
        var nameCol = new VBoxContainer(); nameCol.AddThemeConstantOverride("separation", 2);
        var nameLabel = new Label { Text = "宝宝" };
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.24f, 0.36f, 0.56f));
        var timeLabel = new Label { Text = m.Time };
        timeLabel.AddThemeFontSizeOverride("font_size", 14);
        timeLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.58f));
        nameCol.AddChild(nameLabel); nameCol.AddChild(timeLabel);
        head.AddChild(nameCol);
        col.AddChild(head);

        var text = new Label { Text = m.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        text.AddThemeFontSizeOverride("font_size", 20);
        text.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.17f));
        col.AddChild(text);

        if (!string.IsNullOrEmpty(m.Photo))
        {
            var btn = new Button
            {
                CustomMinimumSize = new Vector2(0, 320),
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            var tex = ResourceLoader.Exists(m.Photo) ? GD.Load<Texture2D>(m.Photo) : null;
            mirrorPhoto = tex;
            if (tex != null)
            {
                var pic = new TextureRect
                {
                    Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                btn.AddChild(pic);
            }
            var hint = new Label
            {
                Text = m.PhotoHint,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            hint.AddThemeFontSizeOverride("font_size", 16);
            hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
            hint.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.5f));
            hint.AddThemeConstantOverride("outline_size", 5);
            btn.AddChild(hint);
            hint.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            hint.OffsetTop = -30; hint.OffsetBottom = -6;

            btn.Pressed += ShowPhotoReveal;
            col.AddChild(btn);
        }
        else if (!string.IsNullOrEmpty(m.PhotoHint))
        {
            var hint = new Label { Text = m.PhotoHint };
            hint.AddThemeFontSizeOverride("font_size", 15);
            hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.58f));
            col.AddChild(hint);
        }

        return card;
    }

    /// <summary>点开照片 → 发现演出</summary>
    private void ShowPhotoReveal()
    {
        if (revealDone)
            return;
        revealDone = true;

        photoOverlay = new Control { MouseFilter = MouseFilterEnum.Stop };
        photoOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(photoOverlay);

        var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0.92f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        photoOverlay.AddChild(bg);

        if (mirrorPhoto != null)
        {
            var pic = new TextureRect
            {
                Texture = mirrorPhoto,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                PivotOffset = Vector2.Zero,
            };
            pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            photoOverlay.AddChild(pic);

            // 发现演出：BGM 停 → 画面缓慢放大 1.0→1.35（用 tween）→ 黑屏 + 一行字
            AudioManager.Instance?.PlayBgm("", 0f, 2.2f);
            var tw = CreateTween();
            tw.TweenProperty(pic, "scale", new Vector2(1.35f, 1.35f), 4.2)
              .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tw.Parallel().TweenProperty(bg, "color", new Color(0f, 0f, 0f, 0.35f), 4.2);
            tw.TweenInterval(0.6);
            tw.TweenProperty(bg, "color", new Color(0f, 0f, 0f, 1f), 1.6);
            tw.TweenCallback(Callable.From(ShowRevealLine));
        }
        photoOverlay.GuiInput += e =>
        {
            if (revealDone && e is InputEventMouseButton mb && mb.Pressed)
                CloseReveal();
        };
    }

    private void ShowRevealLine()
    {
        var label = new Label
        {
            Text = "原来那天，她说在加班。",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.87f));
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        photoOverlay.AddChild(label);

        // 心跳：每 1.1 秒一下，很轻（静下来之后只剩这个）
        var timer = GetTree().CreateTimer(1.1, processAlways: false);
        timer.Timeout += () => AudioManager.Instance?.PlaySfx(AudioManager.SfxConfirmSoft, -24f, 0.08f);
    }

    private void CloseReveal()
    {
        photoOverlay?.QueueFree();
        photoOverlay = null;
        DiscoveredOnce = true;
        Discovered?.Invoke();
    }
}
