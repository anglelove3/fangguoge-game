using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 手机 App 页基类（第十五轮）
///
/// 【公共约定】
///   - 头栏（返回键 + 标题）由 MakeHeader / AddBackAndTitle 统一搭，App 只管内容；
///   - 想回桌面就抛 BackRequested；想弹小提示就抛 ToastRequested（由 ChatOverlay 接到自己的 toast）；
///   - GoBack()：Esc 按下时先问 App"你有没有要关的内部层"（相册大图 / 邮件详情），
///     true = 我关掉了，false = 交还给 ChatOverlay（回桌面）。
/// </summary>
public partial class PhoneAppPage : Control
{
    /// <summary>点返回键（App 自己没有内部层要关时）</summary>
    public event Action BackRequested;
    /// <summary>想弹居中 toast（ChatOverlay 负责显示）</summary>
    public event Action<string> ToastRequested;

    protected void RequestBack() => BackRequested?.Invoke();
    protected void Toast(string text) => ToastRequested?.Invoke(text);

    /// <summary>Esc / 返回键：App 自己的内部层级先消化（默认没有）</summary>
    public virtual bool GoBack() => false;

    /// <summary>页面被打开时调用（默认啥也不做）</summary>
    public virtual void OnShown() { }

    // ---------- 头栏 ----------

    protected (PanelContainer header, Control box, StyleBoxFlat style) MakeHeader(Color bg)
    {
        var header = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = 28,
            CornerRadiusTopRight = 28,
        };
        header.AddThemeStyleboxOverride("panel", style);
        header.AnchorRight = 1f;
        header.OffsetBottom = 100;
        header.GrowHorizontal = GrowDirection.Both;
        AddChild(header);

        var box = new Control { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        header.AddChild(box);
        return (header, box, style);
    }

    /// <summary>返回键 + 居中标题（返回标题 Label，演示页要改字用）</summary>
    protected Label AddBackAndTitle(Control box, string title, Color fg)
    {
        var back = new Button
        {
            Text = "‹",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        back.AddThemeFontSizeOverride("font_size", 46);
        back.AddThemeColorOverride("font_color", fg);
        var empty = new StyleBoxEmpty();
        var hover = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.14f) };
        hover.SetCornerRadiusAll(16);
        var pressed = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.22f) };
        pressed.SetCornerRadiusAll(16);
        back.AddThemeStyleboxOverride("normal", empty);
        back.AddThemeStyleboxOverride("hover", hover);
        back.AddThemeStyleboxOverride("pressed", pressed);
        back.AddThemeStyleboxOverride("focus", empty);
        back.OffsetLeft = 14;
        back.OffsetTop = 4;
        back.OffsetRight = 78;
        back.OffsetBottom = 96;
        back.Pressed += OnHeaderBack;
        box.AddChild(back);

        var label = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 30);
        label.AddThemeColorOverride("font_color", fg);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        label.OffsetLeft = 100;
        label.OffsetRight = -100;
        box.AddChild(label);
        return label;
    }

    protected virtual void OnHeaderBack() => RequestBack();
}

// ==================== 网易云音乐 ====================

/// <summary>
/// 网易云音乐：歌单能点，真的放歌。
/// 手机里的歌一响，章节 BGM 先让位（SetBgmDuck），暂停/收起手机再还回去。
/// </summary>
public partial class MusicAppPage : PhoneAppPage
{
    private static readonly Color NeteaseRed = new(0.878f, 0.200f, 0.200f); // #E03333
    private static readonly Color RowTitleColor = new(0.13f, 0.13f, 0.15f);
    private static readonly Color RowGreyColor = new(0.55f, 0.55f, 0.58f);

    private AudioStreamPlayer player;
    private TextureRect npCover;
    private Label npTitle;
    private Label npArtist;
    private ProgressBar progress;
    private Label timeNow;
    private Label timeTotal;
    private PlayPauseGlyph playGlyph;
    private int currentIndex = -1;
    private string lastShownTime = "";

    private readonly List<Label> rowIndex = new();
    private readonly List<Label> rowTitles = new();
    private readonly List<bool> rowLocked = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        player = new AudioStreamPlayer { VolumeDb = -9f };
        AddChild(player);

        var (_, box, _) = MakeHeader(NeteaseRed);
        AddBackAndTitle(box, PhoneData.App("music")?.Name ?? "网易云音乐", Colors.White);

        BuildNowPlaying();
        BuildSongList();
        UpdateNowPlaying();
    }

    private void BuildNowPlaying()
    {
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = Colors.White };
        style.SetCornerRadiusAll(20);
        style.ShadowColor = new Color(0, 0, 0, 0.10f);
        style.ShadowSize = 8;
        style.ShadowOffset = new Vector2(0, 3);
        card.AddThemeStyleboxOverride("panel", style);
        card.AnchorRight = 1f;
        card.OffsetLeft = 24;
        card.OffsetTop = 112;
        card.OffsetRight = -24;
        card.OffsetBottom = 366;
        card.GrowHorizontal = GrowDirection.Both;
        AddChild(card);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 14);
        card.AddChild(UiKit.WrapMargin(col, 22, 20, 22, 16));

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 20);
        col.AddChild(top);

        npCover = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(140, 140),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        npCover.Material = ChatOverlay.MakeMaskMaterial(140, 140, 16);
        top.AddChild(npCover);

        var info = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        info.AddThemeConstantOverride("separation", 6);
        top.AddChild(info);

        var tag = new Label { Text = DataStore.Text("phone.music.now_playing"), MouseFilter = MouseFilterEnum.Ignore };
        tag.AddThemeFontSizeOverride("font_size", 20);
        tag.AddThemeColorOverride("font_color", NeteaseRed);
        info.AddChild(tag);

        npTitle = new Label { MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        npTitle.AddThemeFontSizeOverride("font_size", 28);
        npTitle.AddThemeColorOverride("font_color", RowTitleColor);
        info.AddChild(npTitle);

        npArtist = new Label { MouseFilter = MouseFilterEnum.Ignore, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        npArtist.AddThemeFontSizeOverride("font_size", 22);
        npArtist.AddThemeColorOverride("font_color", RowGreyColor);
        info.AddChild(npArtist);

        progress = new ProgressBar
        {
            ShowPercentage = false,
            MaxValue = 1,
            CustomMinimumSize = new Vector2(0, 10),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.08f) };
        bgStyle.SetCornerRadiusAll(5);
        var fillStyle = new StyleBoxFlat { BgColor = NeteaseRed };
        fillStyle.SetCornerRadiusAll(5);
        progress.AddThemeStyleboxOverride("background", bgStyle);
        progress.AddThemeStyleboxOverride("fill", fillStyle);
        col.AddChild(progress);

        var times = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(times);
        timeNow = new Label { Text = "00:00", MouseFilter = MouseFilterEnum.Ignore };
        timeNow.AddThemeFontSizeOverride("font_size", 20);
        timeNow.AddThemeColorOverride("font_color", RowGreyColor);
        times.AddChild(timeNow);
        times.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        timeTotal = new Label { Text = "00:00", MouseFilter = MouseFilterEnum.Ignore };
        timeTotal.AddThemeFontSizeOverride("font_size", 20);
        timeTotal.AddThemeColorOverride("font_color", RowGreyColor);
        times.AddChild(timeTotal);

        // ---- 播放 / 暂停圆钮 ----
        var playBtn = new Button
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var circle = new StyleBoxFlat { BgColor = NeteaseRed };
        circle.SetCornerRadiusAll(46);
        var hover = new StyleBoxFlat { BgColor = NeteaseRed.Lightened(0.12f) };
        hover.SetCornerRadiusAll(46);
        var pressedSb = new StyleBoxFlat { BgColor = NeteaseRed.Darkened(0.14f) };
        pressedSb.SetCornerRadiusAll(46);
        playBtn.AddThemeStyleboxOverride("normal", circle);
        playBtn.AddThemeStyleboxOverride("hover", hover);
        playBtn.AddThemeStyleboxOverride("pressed", pressedSb);
        playBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        playBtn.AnchorLeft = 0.5f;
        playBtn.AnchorRight = 0.5f;
        playBtn.OffsetLeft = -46;
        playBtn.OffsetRight = 46;
        playBtn.OffsetTop = 382;
        playBtn.OffsetBottom = 474;
        playBtn.GrowHorizontal = GrowDirection.Both;
        playBtn.Pressed += TogglePlay;
        AddChild(playBtn);

        playGlyph = new PlayPauseGlyph();
        playBtn.AddChild(playGlyph);
    }

    private void BuildSongList()
    {
        var title = new Label { Text = DataStore.Text("phone.music.list_title"), MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.38f));
        title.OffsetLeft = 30;
        title.OffsetTop = 486;
        title.OffsetRight = 300;
        title.OffsetBottom = 520;
        AddChild(title);

        var songs = PhoneData.Data.Music.Songs;
        for (int i = 0; i < songs.Count; i++)
        {
            int idx = i;
            var s = songs[i];

            var row = new Button
            {
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            var normal = new StyleBoxFlat { BgColor = Colors.White };
            normal.SetCornerRadiusAll(16);
            var hover = new StyleBoxFlat { BgColor = new Color(0.955f, 0.955f, 0.965f) };
            hover.SetCornerRadiusAll(16);
            var pressedSb = new StyleBoxFlat { BgColor = new Color(0.91f, 0.91f, 0.92f) };
            pressedSb.SetCornerRadiusAll(16);
            row.AddThemeStyleboxOverride("normal", normal);
            row.AddThemeStyleboxOverride("hover", hover);
            row.AddThemeStyleboxOverride("pressed", pressedSb);
            row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            row.AnchorRight = 1f;
            row.OffsetLeft = 24;
            row.OffsetRight = -24;
            row.OffsetTop = 528 + i * 96;
            row.OffsetBottom = row.OffsetTop + 88;
            row.GrowHorizontal = GrowDirection.Both;
            row.Pressed += () => PlaySong(idx);
            AddChild(row);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 16);
            // 行是 Button（不是容器），内容层必须自己锚定铺满——顺带把内容垂直居中
            var lineWrap = UiKit.WrapMargin(line, 20, 0, 18, 0);
            lineWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.AddChild(lineWrap);

            var num = new Label
            {
                Text = (i + 1).ToString("D2"),
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            num.CustomMinimumSize = new Vector2(50, 0);
            num.AddThemeFontSizeOverride("font_size", 24);
            num.AddThemeColorOverride("font_color", RowGreyColor);
            line.AddChild(num);
            rowIndex.Add(num);

            var col = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            col.AddThemeConstantOverride("separation", 2);
            line.AddChild(col);

            var t = new Label
            {
                Text = s.Title,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            t.AddThemeFontSizeOverride("font_size", 26);
            t.AddThemeColorOverride("font_color", RowTitleColor);
            col.AddChild(t);
            rowTitles.Add(t);

            var a = new Label
            {
                Text = s.Artist,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            a.AddThemeFontSizeOverride("font_size", 21);
            a.AddThemeColorOverride("font_color", RowGreyColor);
            col.AddChild(a);

            if (s.Locked)
            {
                var lockedTag = new Label
                {
                    Text = DataStore.Text("phone.music.locked_tag"),
                    VerticalAlignment = VerticalAlignment.Center,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                lockedTag.AddThemeFontSizeOverride("font_size", 20);
                lockedTag.AddThemeColorOverride("font_color", new Color(0.72f, 0.72f, 0.75f));
                line.AddChild(lockedTag);
            }
            rowLocked.Add(s.Locked);
        }
    }

    // ---------- 播放 ----------

    private void PlaySong(int index)
    {
        var songs = PhoneData.Data.Music.Songs;
        if (index < 0 || index >= songs.Count)
            return;
        var s = songs[index];

        var loaded = !string.IsNullOrEmpty(s.File) ? GD.Load<AudioStream>(s.File) : null;
        if (s.Locked || loaded == null)
        {
            Toast(!string.IsNullOrEmpty(s.Hint) ? s.Hint : DataStore.Text("phone.music.locked_tag"));
            return;
        }

        // 复制一份流再开循环：AudioManager 拿的是同一份缓存资源，直接改会串味——
        // 手机里的歌单曲循环，BGM 那边保持原样。
        var dup = loaded.Duplicate() as AudioStream;
        if (dup is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        player.Stop();
        player.Stream = dup;
        player.Play();
        currentIndex = index;
        playGlyph.ShowPause = true;
        timeTotal.Text = FormatTime((float)dup.GetLength());
        lastShownTime = "";
        // 手机里的歌一响，章节 BGM 让位（收起手机 / 暂停时还回去）
        AudioManager.Instance?.SetBgmDuck(-60f);
        UpdateNowPlaying();
    }

    private void TogglePlay()
    {
        if (player == null)
            return;
        if (player.Playing)
        {
            player.StreamPaused = true;
            playGlyph.ShowPause = false;
            AudioManager.Instance?.SetBgmDuck(0f);
            return;
        }
        if (player.StreamPaused)
        {
            player.StreamPaused = false;
            playGlyph.ShowPause = true;
            AudioManager.Instance?.SetBgmDuck(-60f);
            return;
        }
        PlaySong(currentIndex >= 0 ? currentIndex : 0);
    }

    public override void _Process(double delta)
    {
        if (player == null || player.Stream == null || !player.Playing)
            return;
        double len = player.Stream.GetLength();
        if (len <= 0.01)
            return;
        progress.MaxValue = len;
        progress.Value = player.GetPlaybackPosition();
        string now = FormatTime((float)player.GetPlaybackPosition());
        if (now != lastShownTime)
        {
            lastShownTime = now;
            timeNow.Text = now;
        }
    }

    public override void _ExitTree()
    {
        player?.Stop();
        AudioManager.Instance?.SetBgmDuck(0f);
    }

    private static string FormatTime(float seconds)
    {
        int t = (int)Mathf.Max(0f, seconds);
        return $"{t / 60:D2}:{t % 60:D2}";
    }

    private void UpdateNowPlaying()
    {
        var songs = PhoneData.Data.Music.Songs;
        var s = currentIndex >= 0 && currentIndex < songs.Count ? songs[currentIndex] : songs.Count > 0 ? songs[0] : null;
        if (s == null)
            return;
        npTitle.Text = string.IsNullOrEmpty(s.Title) ? DataStore.Text("phone.music.nothing") : s.Title;
        npArtist.Text = s.Artist;
        bool hasCover = !string.IsNullOrEmpty(s.Cover) && ResourceLoader.Exists(s.Cover);
        npCover.Texture = hasCover ? GD.Load<Texture2D>(s.Cover) : null;
        npCover.Modulate = currentIndex >= 0 ? Colors.White : new Color(1, 1, 1, 0.55f);

        for (int i = 0; i < rowTitles.Count; i++)
        {
            bool playing = i == currentIndex;
            bool locked = i < rowLocked.Count && rowLocked[i];
            Color titleColor = locked ? new Color(0.62f, 0.62f, 0.65f)
                : playing ? NeteaseRed : RowTitleColor;
            Color indexColor = locked ? new Color(0.72f, 0.72f, 0.75f)
                : playing ? NeteaseRed : RowGreyColor;
            rowTitles[i].AddThemeColorOverride("font_color", titleColor);
            rowIndex[i].AddThemeColorOverride("font_color", indexColor);
        }
    }
}

/// <summary>会自己画画的小图标：▶（暂停态显示）/ ⏸（播放态显示，两根白条）</summary>
public partial class PlayPauseGlyph : Control
{
    private bool showPause;

    public bool ShowPause
    {
        get => showPause;
        set
        {
            if (showPause == value)
                return;
            showPause = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        if (showPause)
        {
            var bar = new StyleBoxFlat { BgColor = Colors.White };
            bar.SetCornerRadiusAll(3);
            DrawStyleBox(bar, new Rect2(w * 0.37f, h * 0.33f, w * 0.09f, h * 0.34f));
            DrawStyleBox(bar, new Rect2(w * 0.54f, h * 0.33f, w * 0.09f, h * 0.34f));
        }
        else
        {
            DrawColoredPolygon(new Vector2[]
            {
                new(w * 0.39f, h * 0.31f),
                new(w * 0.39f, h * 0.69f),
                new(w * 0.69f, h * 0.50f),
            }, Colors.White);
        }
    }
}

// ==================== 相册 ====================

/// <summary>相册：九宫格 + 点开看大图（配一句注脚，点任意处返回）</summary>
public partial class AlbumAppPage : PhoneAppPage
{
    private static readonly Color HeaderGrey = new(0.16f, 0.17f, 0.20f);

    private Control viewer;
    private TextureRect viewerImage;
    private Label viewerCaption;
    private readonly List<PhoneData.PhotoEntry> photos = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var (_, box, _) = MakeHeader(HeaderGrey);
        AddBackAndTitle(box, PhoneData.App("album")?.Name ?? "相册", Colors.White);

        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        pad.AnchorRight = 1f;
        pad.AnchorBottom = 1f;
        pad.OffsetTop = 116;
        pad.OffsetBottom = -18;
        pad.GrowHorizontal = GrowDirection.Both;
        pad.GrowVertical = GrowDirection.Both;
        pad.AddThemeConstantOverride("margin_left", 46);
        pad.AddThemeConstantOverride("margin_right", 46);
        AddChild(pad);

        var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        pad.AddChild(grid);

        photos.Clear();
        photos.AddRange(PhoneData.Data.Album);
        for (int i = 0; i < photos.Count; i++)
        {
            int idx = i;
            var p = photos[i];
            var tile = new Button
            {
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(214, 214),
            };
            var empty = new StyleBoxEmpty();
            var hover = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.10f) };
            hover.SetCornerRadiusAll(14);
            var pressedSb = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.18f) };
            pressedSb.SetCornerRadiusAll(14);
            tile.AddThemeStyleboxOverride("normal", empty);
            tile.AddThemeStyleboxOverride("hover", hover);
            tile.AddThemeStyleboxOverride("pressed", pressedSb);
            tile.AddThemeStyleboxOverride("focus", empty);
            tile.Pressed += () => OpenViewer(idx);

            if (!string.IsNullOrEmpty(p.Photo) && ResourceLoader.Exists(p.Photo))
            {
                var tex = new TextureRect
                {
                    Texture = GD.Load<Texture2D>(p.Photo),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                tex.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                tex.Material = ChatOverlay.MakeMaskMaterial(214, 214, 14);
                tile.AddChild(tex);
            }
            grid.AddChild(tile);
        }

        BuildViewer();
    }

    private void BuildViewer()
    {
        viewer = new Control { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        viewer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(viewer);

        var dark = new ColorRect
        {
            Color = new Color(0.04f, 0.04f, 0.06f, 0.97f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dark.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                AcceptEvent();
                CloseViewer();
            }
        };
        viewer.AddChild(dark);

        viewerImage = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewerImage.AnchorRight = 1f;
        viewerImage.AnchorBottom = 1f;
        viewerImage.OffsetLeft = 42;
        viewerImage.OffsetTop = 120;
        viewerImage.OffsetRight = -42;
        viewerImage.OffsetBottom = -258;
        viewerImage.GrowHorizontal = GrowDirection.Both;
        viewerImage.GrowVertical = GrowDirection.Both;
        viewer.AddChild(viewerImage);

        viewerCaption = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        viewerCaption.AddThemeFontSizeOverride("font_size", 26);
        viewerCaption.AddThemeColorOverride("font_color", new Color(0.97f, 0.96f, 0.94f));
        viewerCaption.AnchorLeft = 0.5f;
        viewerCaption.AnchorRight = 0.5f;
        viewerCaption.AnchorTop = 1f;
        viewerCaption.AnchorBottom = 1f;
        viewerCaption.OffsetLeft = -320;
        viewerCaption.OffsetRight = 320;
        viewerCaption.OffsetTop = -226;
        viewerCaption.OffsetBottom = -140;
        viewerCaption.GrowHorizontal = GrowDirection.Both;
        viewerCaption.GrowVertical = GrowDirection.Begin;
        viewer.AddChild(viewerCaption);

        var hint = new Label
        {
            Text = DataStore.Text("phone.album.viewer_hint"),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hint.AddThemeFontSizeOverride("font_size", 20);
        hint.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.74f));
        hint.AnchorLeft = 0.5f;
        hint.AnchorRight = 0.5f;
        hint.AnchorTop = 1f;
        hint.AnchorBottom = 1f;
        hint.OffsetLeft = -200;
        hint.OffsetRight = 200;
        hint.OffsetTop = -104;
        hint.OffsetBottom = -64;
        hint.GrowHorizontal = GrowDirection.Both;
        hint.GrowVertical = GrowDirection.Begin;
        viewer.AddChild(hint);
    }

    private void OpenViewer(int index)
    {
        if (index < 0 || index >= photos.Count)
            return;
        var p = photos[index];
        viewerImage.Texture = !string.IsNullOrEmpty(p.Photo) && ResourceLoader.Exists(p.Photo)
            ? GD.Load<Texture2D>(p.Photo)
            : null;
        viewerCaption.Text = p.Caption;
        viewer.Visible = true;
        viewer.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(viewer, "modulate:a", 1f, 0.16);
    }

    private void CloseViewer()
    {
        viewer.Visible = false;
    }

    public override bool GoBack()
    {
        if (viewer != null && viewer.Visible)
        {
            CloseViewer();
            return true;
        }
        return false;
    }
}

// ==================== QQ邮箱 ====================

/// <summary>QQ邮箱：收件箱列表 → 点开读信（读过的小蓝点会消）</summary>
public partial class MailAppPage : PhoneAppPage
{
    private static readonly Color QqBlue = new(0.180f, 0.486f, 0.839f); // #2E7CD6
    private static readonly Color MailDark = new(0.13f, 0.13f, 0.15f);
    private static readonly Color MailGrey = new(0.55f, 0.55f, 0.58f);

    /// <summary>读过哪些信（本局内记得：from|subject）</summary>
    private static readonly HashSet<string> readKeys = new();

    private Control listView;
    private VBoxContainer listBox;
    private Control detailView;
    private Label dSubject;
    private Label dMeta;
    private VBoxContainer dBody;
    private bool detailOpen;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var (_, box, _) = MakeHeader(QqBlue);
        AddBackAndTitle(box, PhoneData.App("mail")?.Name ?? "QQ邮箱", Colors.White);

        // ---- 列表视图 ----
        listView = new Control { MouseFilter = MouseFilterEnum.Ignore };
        listView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        listView.OffsetTop = 116;
        AddChild(listView);

        listBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        listBox.AddThemeConstantOverride("separation", 14);
        // 包一层边距 + 铺满（listView 是普通 Control 不是容器，光 WrapMargin 不会撑开宽度）
        var listWrap = UiKit.WrapMargin(listBox, 26, 0, 26, 20);
        listWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        listView.AddChild(listWrap);

        // ---- 详情视图 ----
        detailView = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        detailView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        detailView.OffsetTop = 116;
        AddChild(detailView);

        var dcol = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        dcol.AddThemeConstantOverride("separation", 14);
        var dWrap = UiKit.WrapMargin(dcol, 40, 8, 40, 28);
        dWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        detailView.AddChild(dWrap);

        dSubject = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        dSubject.AddThemeFontSizeOverride("font_size", 32);
        dSubject.AddThemeColorOverride("font_color", MailDark);
        dcol.AddChild(dSubject);

        dMeta = new Label { MouseFilter = MouseFilterEnum.Ignore };
        dMeta.AddThemeFontSizeOverride("font_size", 22);
        dMeta.AddThemeColorOverride("font_color", MailGrey);
        dcol.AddChild(dMeta);

        var sep = new ColorRect { Color = new Color(0, 0, 0, 0.08f), MouseFilter = MouseFilterEnum.Ignore };
        sep.CustomMinimumSize = new Vector2(0, 1);
        dcol.AddChild(sep);

        dBody = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        dBody.AddThemeConstantOverride("separation", 12);
        dcol.AddChild(dBody);

        RebuildList();
    }

    private static string MailKey(PhoneData.MailEntry m) => $"{m.From}|{m.Subject}";

    private void RebuildList()
    {
        foreach (var child in listBox.GetChildren())
        {
            if (child is Control c)
                c.Visible = false;
            child.QueueFree();
        }

        foreach (var m in PhoneData.Data.Mail)
        {
            var row = new Button
            {
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 126),
            };
            var normal = new StyleBoxFlat { BgColor = Colors.White };
            normal.SetCornerRadiusAll(16);
            normal.ShadowColor = new Color(0, 0, 0, 0.06f);
            normal.ShadowSize = 5;
            normal.ShadowOffset = new Vector2(0, 2);
            var hover = new StyleBoxFlat { BgColor = new Color(0.955f, 0.955f, 0.965f) };
            hover.SetCornerRadiusAll(16);
            var pressedSb = new StyleBoxFlat { BgColor = new Color(0.91f, 0.91f, 0.92f) };
            pressedSb.SetCornerRadiusAll(16);
            row.AddThemeStyleboxOverride("normal", normal);
            row.AddThemeStyleboxOverride("hover", hover);
            row.AddThemeStyleboxOverride("pressed", pressedSb);
            row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            row.Pressed += () => OpenMail(m);
            UiSounds.Wire(row); // 行是动态建的，补挂音效
            listBox.AddChild(row);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 18);
            row.AddChild(UiKit.WrapMargin(line, 22, 16, 22, 16));

            bool unread = m.Unread && !readKeys.Contains(MailKey(m));
            var dot = new PanelContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = unread,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(14, 14),
            };
            var dotStyle = new StyleBoxFlat { BgColor = QqBlue };
            dotStyle.SetCornerRadiusAll(7);
            dot.AddThemeStyleboxOverride("panel", dotStyle);
            line.AddChild(dot);

            var col = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            col.AddThemeConstantOverride("separation", 4);
            line.AddChild(col);

            var from = new Label { Text = m.From, MouseFilter = MouseFilterEnum.Ignore };
            from.AddThemeFontSizeOverride("font_size", 27);
            from.AddThemeColorOverride("font_color", MailDark);
            col.AddChild(from);

            var subject = new Label
            {
                Text = m.Subject,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            subject.AddThemeFontSizeOverride("font_size", 23);
            subject.AddThemeColorOverride("font_color", unread ? new Color(0.30f, 0.30f, 0.33f) : MailGrey);
            col.AddChild(subject);

            var time = new Label
            {
                Text = m.Time,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            time.AddThemeFontSizeOverride("font_size", 20);
            time.AddThemeColorOverride("font_color", MailGrey);
            line.AddChild(time);
        }
    }

    private void OpenMail(PhoneData.MailEntry m)
    {
        readKeys.Add(MailKey(m));
        dSubject.Text = m.Subject;
        dMeta.Text = $"{m.From} · {m.Time}";
        foreach (var c in dBody.GetChildren())
        {
            if (c is Control ctrl)
                ctrl.Visible = false;
            c.QueueFree();
        }
        foreach (var para in m.Body)
        {
            var l = new Label
            {
                Text = para,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            l.AddThemeFontSizeOverride("font_size", 26);
            l.AddThemeConstantOverride("line_spacing", 8);
            l.AddThemeColorOverride("font_color", new Color(0.16f, 0.16f, 0.18f));
            dBody.AddChild(l);
        }
        detailOpen = true;
        listView.Visible = false;
        detailView.Visible = true;
    }

    private void ShowList()
    {
        detailOpen = false;
        detailView.Visible = false;
        listView.Visible = true;
        RebuildList(); // 红点按最新已读状态重画
    }

    protected override void OnHeaderBack()
    {
        if (detailOpen)
        {
            ShowList();
            return;
        }
        RequestBack();
    }

    public override bool GoBack()
    {
        if (detailOpen)
        {
            ShowList();
            return true;
        }
        return false;
    }
}

// ==================== DeepSeek ====================

/// <summary>DeepSeek：选个问题问它，它"思考"一会儿再一字一字答</summary>
public partial class AiChatPage : PhoneAppPage
{
    private static readonly Color DeepBlue = new(0.239f, 0.357f, 0.878f); // #3D5BE0
    private static readonly Color AiTextColor = new(0.12f, 0.12f, 0.14f);

    private ScrollContainer scroll;
    private VBoxContainer rows;
    private PanelContainer thinkingChip;
    private Control thinkingRow;
    private Tween thinkingTween;
    private readonly List<Button> chips = new();
    private readonly List<bool> chipUsed = new();
    private PanelContainer inputBar;

    private bool thinking;
    private bool revealing;
    private Label revealLabel;
    private string revealFull = "";
    private float revealTimer;
    private int revealedChars;
    private const float CharsPerSecond = 46f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var (_, box, _) = MakeHeader(DeepBlue);
        AddBackAndTitle(box, PhoneData.App("ai")?.Name ?? "DeepSeek", Colors.White);

        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AnchorRight = 1f;
        scroll.AnchorBottom = 1f;
        scroll.OffsetTop = 102;
        scroll.OffsetBottom = -128;
        scroll.GrowHorizontal = GrowDirection.Both;
        scroll.GrowVertical = GrowDirection.Both;
        AddChild(scroll);

        var pad = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        pad.AddThemeConstantOverride("margin_left", 24);
        pad.AddThemeConstantOverride("margin_right", 24);
        pad.AddThemeConstantOverride("margin_top", 26);
        pad.AddThemeConstantOverride("margin_bottom", 20);
        scroll.AddChild(pad);

        rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 16);
        pad.AddChild(rows);

        // 开场白
        AppendAiBubble(PhoneData.Data.Ai.Greeting);

        // 提问快捷条
        var chipTitle = new Label
        {
            Text = DataStore.Text("phone.ai.chips_title"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chipTitle.AddThemeFontSizeOverride("font_size", 22);
        chipTitle.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        var chipTitlePad = UiKit.WrapMargin(chipTitle, 0, 8, 0, 0);
        rows.AddChild(chipTitlePad);

        var qs = PhoneData.Data.Ai.Questions;
        for (int i = 0; i < qs.Count; i++)
        {
            int idx = i;
            var chip = new Button
            {
                Text = qs[i].Q,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 62),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            var normal = new StyleBoxFlat { BgColor = Colors.White };
            normal.SetCornerRadiusAll(18);
            normal.SetBorderWidthAll(1);
            normal.BorderColor = new Color(0.34f, 0.42f, 0.58f, 0.35f);
            var hover = new StyleBoxFlat { BgColor = new Color(0.93f, 0.95f, 0.99f) };
            hover.SetCornerRadiusAll(18);
            hover.SetBorderWidthAll(1);
            hover.BorderColor = new Color(0.34f, 0.42f, 0.58f, 0.5f);
            var pressedSb = new StyleBoxFlat { BgColor = new Color(0.87f, 0.90f, 0.96f) };
            pressedSb.SetCornerRadiusAll(18);
            chip.AddThemeStyleboxOverride("normal", normal);
            chip.AddThemeStyleboxOverride("hover", hover);
            chip.AddThemeStyleboxOverride("pressed", pressedSb);
            chip.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            chip.AddThemeFontSizeOverride("font_size", 24);
            chip.AddThemeColorOverride("font_color", new Color(0.34f, 0.42f, 0.58f));
            chip.Pressed += () => Ask(idx);
            rows.AddChild(chip);
            chips.Add(chip);
            chipUsed.Add(false);
        }

        // ---- 假输入栏（点一下弹 toast）----
        inputBar = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var inputStyle = new StyleBoxFlat { BgColor = Colors.White };
        inputStyle.SetCornerRadiusAll(22);
        inputStyle.ShadowColor = new Color(0, 0, 0, 0.08f);
        inputStyle.ShadowSize = 6;
        inputStyle.ShadowOffset = new Vector2(0, 2);
        inputBar.AddThemeStyleboxOverride("panel", inputStyle);
        inputBar.AnchorLeft = 0f;
        inputBar.AnchorRight = 1f;
        inputBar.AnchorTop = 1f;
        inputBar.AnchorBottom = 1f;
        inputBar.OffsetLeft = 24;
        inputBar.OffsetRight = -24;
        inputBar.OffsetTop = -112;
        inputBar.OffsetBottom = -30;
        inputBar.GrowHorizontal = GrowDirection.Both;
        inputBar.GrowVertical = GrowDirection.Begin;
        inputBar.GuiInput += e =>
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                AcceptEvent();
                Toast(DataStore.Text("phone.ai.input_toast"));
            }
        };
        AddChild(inputBar);

        var placeholder = new Label
        {
            Text = DataStore.Text("phone.ai.placeholder"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        placeholder.AddThemeFontSizeOverride("font_size", 24);
        placeholder.AddThemeColorOverride("font_color", new Color(0.62f, 0.62f, 0.65f));
        inputBar.AddChild(UiKit.WrapMargin(placeholder, 22, 0, 22, 0));
    }

    // ---------- 消息 ----------

    private static Control MakeAiAvatar()
    {
        var avatar = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(52, 52),
        };
        var st = new StyleBoxFlat { BgColor = new Color(0.16f, 0.36f, 0.94f) };
        st.SetCornerRadiusAll(26);
        avatar.AddThemeStyleboxOverride("panel", st);
        var label = new Label
        {
            Text = "深",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", Colors.White);
        avatar.AddChild(label);
        return avatar;
    }

    /// <summary>
    /// 按文字量给气泡定宽：宽了自动换行、窄了随字生长。
    /// 【坑】纯中文的 Autowrap Label 最小宽度≈一个字——空文本建出来的气泡
    /// 不打这个补丁就永远是一列竖条（打字机动画每帧都要重算）。
    /// </summary>
    private void FitBubbleWidth(Label label, string text)
    {
        float w = GetThemeDefaultFont()
            .GetStringSize(text, HorizontalAlignment.Left, -1, 26).X;
        label.CustomMinimumSize = new Vector2(Mathf.Min(w + 4f, 540f), 0);
    }

    private (PanelContainer bubble, Label label) MakeBubble(string text, bool mine)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 26);
        label.AddThemeConstantOverride("line_spacing", 6);
        label.AddThemeColorOverride("font_color", mine ? Colors.White : AiTextColor);
        FitBubbleWidth(label, text);

        var bubble = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        var style = new StyleBoxFlat { BgColor = mine ? DeepBlue : Colors.White };
        style.SetCornerRadiusAll(16);
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        style.ContentMarginTop = 14;
        style.ContentMarginBottom = 14;
        if (mine)
            style.CornerRadiusTopRight = 4;
        else
            style.CornerRadiusTopLeft = 4;
        style.ShadowColor = new Color(0, 0, 0, 0.08f);
        style.ShadowSize = 3;
        style.ShadowOffset = new Vector2(0, 1);
        bubble.AddThemeStyleboxOverride("panel", style);
        bubble.AddChild(label);
        return (bubble, label);
    }

    private Label AppendAiBubble(string text)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(MakeAiAvatar());
        var (bubble, label) = MakeBubble(text, false);
        row.AddChild(bubble);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        rows.AddChild(row);
        return label;
    }

    private void AppendMineBubble(string text)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var (bubble, _) = MakeBubble(text, true);
        row.AddChild(bubble);
        rows.AddChild(row);
    }

    // ---------- 问与答 ----------

    private void Ask(int index)
    {
        if (thinking || revealing)
            return;
        var qs = PhoneData.Data.Ai.Questions;
        if (index < 0 || index >= qs.Count)
            return;

        thinking = true;
        chipUsed[index] = true;
        RefreshChips();

        var q = qs[index];
        AppendMineBubble(q.Q);
        ShowThinking();
        ScrollDownSoon();

        GetTree().CreateTimer(0.9).Timeout += () =>
        {
            if (!IsInsideTree())
                return;
            HideThinking();
            thinking = false;

            string full = q.A ?? "";
            var label = AppendAiBubble("");
            revealLabel = label;
            revealFull = full;
            revealedChars = 0;
            revealTimer = 0f;
            revealing = full.Length > 0;
            RefreshChips();
        };
    }

    private void ShowThinking()
    {
        thinkingRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        ((HBoxContainer)thinkingRow).AddThemeConstantOverride("separation", 12);
        thinkingRow.AddChild(MakeAiAvatar());

        thinkingChip = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        var style = new StyleBoxFlat { BgColor = Colors.White };
        style.SetCornerRadiusAll(16);
        style.ContentMarginLeft = 20;
        style.ContentMarginRight = 20;
        style.ContentMarginTop = 13;
        style.ContentMarginBottom = 13;
        thinkingChip.AddThemeStyleboxOverride("panel", style);
        var label = new Label
        {
            Text = DataStore.Text("phone.ai.thinking"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        thinkingChip.AddChild(label);
        thinkingRow.AddChild(thinkingChip);
        thinkingRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        rows.AddChild(thinkingRow);

        thinkingTween?.Kill();
        thinkingTween = CreateTween();
        thinkingTween.SetLoops(-1);
        thinkingTween.TweenProperty(thinkingChip, "modulate:a", 0.5f, 0.5).SetTrans(Tween.TransitionType.Sine);
        thinkingTween.TweenProperty(thinkingChip, "modulate:a", 1f, 0.5).SetTrans(Tween.TransitionType.Sine);
    }

    private void HideThinking()
    {
        thinkingTween?.Kill();
        thinkingTween = null;
        if (thinkingRow != null)
        {
            thinkingRow.Visible = false;
            thinkingRow.QueueFree();
            thinkingRow = null;
        }
        thinkingChip = null;
    }

    private void RefreshChips()
    {
        for (int i = 0; i < chips.Count; i++)
        {
            bool busy = thinking || revealing;
            bool used = i < chipUsed.Count && chipUsed[i];
            chips[i].Disabled = busy || used;
            chips[i].Modulate = used ? new Color(1, 1, 1, 0.55f) : Colors.White;
        }
    }

    private void ScrollDownSoon()
    {
        GetTree().CreateTimer(0.05).Timeout += () =>
        {
            if (IsInsideTree() && scroll != null)
                scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
        };
    }

    public override void OnShown() => ScrollDownSoon();

    public override void _Process(double delta)
    {
        if (!revealing || revealLabel == null)
            return;
        revealTimer += (float)delta;
        int target = Mathf.Min(revealFull.Length, (int)(revealTimer * CharsPerSecond));
        if (target != revealedChars)
        {
            revealedChars = target;
            revealLabel.Text = revealFull.Substring(0, revealedChars);
            FitBubbleWidth(revealLabel, revealLabel.Text); // 气泡跟着字数长
            if (revealedChars % 10 == 0)
                scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
        }
        if (revealedChars >= revealFull.Length)
        {
            revealing = false;
            revealLabel = null;
            RefreshChips();
            ScrollDownSoon();
        }
    }

    public override bool GoBack()
    {
        if (revealing)
        {
            // 别急着走：先把这句一次说完
            revealedChars = revealFull.Length;
            revealLabel.Text = revealFull;
            FitBubbleWidth(revealLabel, revealFull);
            revealing = false;
            revealLabel = null;
            RefreshChips();
            return true;
        }
        return false;
    }
}

// ==================== 演示页（抖音/百度/支付宝/美团/小红书/QQ/淘宝） ====================

/// <summary>演示 App：换个头像板 + 几行彩蛋文案，看看就好</summary>
public partial class DemoAppPage : PhoneAppPage
{
    private StyleBoxFlat headerStyle;
    private Label titleLabel;
    private Control bodyHost;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var (_, box, style) = MakeHeader(new Color(0.30f, 0.30f, 0.35f));
        headerStyle = style;
        titleLabel = AddBackAndTitle(box, "", Colors.White);

        bodyHost = new Control { MouseFilter = MouseFilterEnum.Ignore };
        bodyHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bodyHost.OffsetTop = 100;
        AddChild(bodyHost);
    }

    /// <summary>按桌面点进来的 App 定义整页刷新</summary>
    public void Open(PhoneData.AppEntry def)
    {
        if (def == null)
            return;

        var baseColor = PhoneData.ParseColor(def.Color);
        headerStyle.BgColor = baseColor.Darkened(0.30f);
        titleLabel.Text = def.Name;

        foreach (var c in bodyHost.GetChildren())
        {
            if (c is Control ctrl)
                ctrl.Visible = false;
            c.QueueFree();
        }

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 26);
        // 铺满 bodyHost（普通 Control）；不锚定的话整列会缩成最小宽度挤在左上角
        var bodyWrap = UiKit.WrapMargin(col, 56, 36, 56, 28);
        bodyWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bodyHost.AddChild(bodyWrap);

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        // 大图标块
        var tileWrap = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(tileWrap);
        var tile = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(148, 148) };
        var tileStyle = new StyleBoxFlat { BgColor = baseColor };
        tileStyle.SetCornerRadiusAll(36);
        tileStyle.ShadowColor = new Color(0, 0, 0, 0.15f);
        tileStyle.ShadowSize = 10;
        tileStyle.ShadowOffset = new Vector2(0, 4);
        tile.AddThemeStyleboxOverride("panel", tileStyle);
        tileWrap.AddChild(tile);
        var glyph = new Label
        {
            Text = def.Glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.AddThemeFontSizeOverride("font_size", 66);
        glyph.AddThemeColorOverride("font_color", def.DarkGlyph ? new Color(0.15f, 0.12f, 0.06f) : Colors.White);
        tile.AddChild(glyph);

        // 彩蛋文案卡
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var cardStyle = new StyleBoxFlat { BgColor = Colors.White };
        cardStyle.SetCornerRadiusAll(20);
        cardStyle.ShadowColor = new Color(0, 0, 0, 0.08f);
        cardStyle.ShadowSize = 6;
        cardStyle.ShadowOffset = new Vector2(0, 3);
        card.AddThemeStyleboxOverride("panel", cardStyle);
        col.AddChild(card);

        var linesBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        linesBox.AddThemeConstantOverride("separation", 14);
        card.AddChild(UiKit.WrapMargin(linesBox, 28, 24, 28, 24));
        foreach (var line in def.Lines)
        {
            var l = new Label
            {
                Text = line,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            l.AddThemeFontSizeOverride("font_size", 26);
            l.AddThemeConstantOverride("line_spacing", 8);
            l.AddThemeColorOverride("font_color", new Color(0.16f, 0.16f, 0.18f));
            linesBox.AddChild(l);
        }

        // 页脚小字
        var footer = new Label
        {
            Text = DataStore.Text("phone.demo_footer"),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        footer.AddThemeFontSizeOverride("font_size", 20);
        footer.AddThemeColorOverride("font_color", new Color(0.58f, 0.58f, 0.62f));
        col.AddChild(footer);

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
    }
}
