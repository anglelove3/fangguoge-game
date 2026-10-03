using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 手机聊天界面（引擎绘制版 · 微信风格）
///
/// 【为什么用"引擎绘制"而不是一张 AI 生成的聊天截图】
///   - 中文清晰锐利，不管什么分辨率都不会糊
///   - 聊天内容放在 data/chat/*.json 里，改台词不碰代码
///   - 可以真的"往上滑"，玩家能一条一条翻旧消息（剧情里的动作）
///
/// 【结构】
///   ChatOverlay（本场景）
///   ├─ Backdrop   半透明黑幕（点它 = 收起手机）
///   ├─ PhonePanel 手机外壳（微信聊天页：头部 / 消息列表 / 底部输入栏）
///   └─ CloseChip  "收起手机"按钮
///
/// 用法：
///   var overlay = scene.Instantiate<ChatOverlay>();
///   AddChild(overlay);
///   overlay.Closed += 关掉之后要做的事;
///   overlay.Open("ch02_phone");   // 对应 data/chat/ch02_phone.json
/// </summary>
public partial class ChatOverlay : Control
{
    /// <summary>界面收起（fade 结束、即将销毁）时触发</summary>
    public event Action Closed;

    // ---------- 微信风配色 ----------
    private static readonly Color MyBubbleColor = new Color(0.585f, 0.925f, 0.41f); // 我：微信绿
    private static readonly Color HerBubbleColor = new Color(1f, 1f, 1f);           // 她：白
    private static readonly Color TextColor = new Color(0.09f, 0.09f, 0.11f);
    private static readonly Color DividerColor = new Color(0.55f, 0.55f, 0.58f);

    private const int FontSize = 28;
    private const float MaxTextBubbleWidth = 540f; // 气泡最宽（超过就换行）
    private const float ImageBubbleWidth = 380f;

    // 遮罩着色器只加载一次（静态缓存）
    private static Shader roundedMaskShader;

    private ScrollContainer scroll;
    private VBoxContainer rows;
    private Control hintChip;
    private Label nameLabel;
    private Panel phonePanel;

    private bool closing;    // 正在收起（防止重复触发）
    private bool hintArmed;  // "滑一滑"提示是否已武装（入场滚动不算）

    public override void _Ready()
    {
        scroll = GetNode<ScrollContainer>("PhonePanel/Scroll");
        rows = GetNode<VBoxContainer>("PhonePanel/Scroll/Pad/Rows");
        hintChip = GetNode<Control>("PhonePanel/HintChip");
        nameLabel = GetNode<Label>("PhonePanel/Header/NameLabel");
        phonePanel = GetNode<Panel>("PhonePanel");

        // 点黑幕 = 收起手机
        GetNode<ColorRect>("Backdrop").GuiInput += OnBackdropInput;

        // "收起手机"按钮
        GetNode<Button>("CloseChip").Pressed += Close;

        // 玩家一滑动，提示气泡就淡出（入场时的自动滚动不算）
        scroll.GetVScrollBar().ValueChanged += OnScrolled;

        // 按钮音效
        UiSounds.WireAll(this);
    }

    // ==================== 对外 API ====================

    /// <summary>
    /// 打开聊天界面，加载 data/chat/{chatId}.json 里的聊天记录
    /// </summary>
    public void Open(string chatId)
    {
        var data = LoadChatData(chatId);
        nameLabel.Text = data.Title;

        // 生成所有消息行
        foreach (var msg in data.Messages)
            rows.AddChild(MakeMessageRow(msg));

        // 打开时直接停在"最新消息"（底部）——和真实微信一样
        ScrollToBottom();

        // 入场动画：整体淡入 + 手机从下方轻轻滑上来
        Modulate = new Color(1, 1, 1, 0);
        PlayEnterAnimation();

        // 1.2 秒后武装提示监听（避开入场自动滚动）
        hintArmed = false;
        GetTree().CreateTimer(1.2).Timeout += () => hintArmed = true;
    }

    /// <summary>收起手机（fade 后销毁，并通知外面）</summary>
    public void Close()
    {
        if (closing)
            return;
        closing = true;

        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.18);
        tween.TweenCallback(Callable.From(() =>
        {
            Closed?.Invoke();
            QueueFree();
        }));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) // Esc / 手柄 B
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    // ==================== 内部逻辑 ====================

    /// <summary>入场动画（等一帧布局完成后再滑，避免位置跳动）</summary>
    private async void PlayEnterAnimation()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree() || closing)
            return;

        var targetPos = phonePanel.Position;
        phonePanel.Position = targetPos + new Vector2(0, 60);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(this, "modulate:a", 1f, 0.25);
        tween.TweenProperty(phonePanel, "position", targetPos, 0.4)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>滚到底部（要等两帧：容器排完版滚动条才知道有多长）</summary>
    private async void ScrollToBottom()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree() || closing)
            return;
        scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
    }

    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            Close();
    }

    /// <summary>玩家一滑动 → 提示气泡淡出（只触发一次）</summary>
    private void OnScrolled(double value)
    {
        if (!hintArmed || closing || !hintChip.Visible)
            return;

        var tween = CreateTween();
        tween.TweenProperty(hintChip, "modulate:a", 0f, 0.4);
        tween.TweenCallback(Callable.From(() => hintChip.Visible = false));
    }

    // ==================== 消息行生成 ====================

    private Control MakeMessageRow(ChatMessageData msg)
    {
        if (msg.Type == "divider")
            return MakeDivider(msg.Text);

        bool mine = msg.Sender == "me";

        var row = new HBoxContainer();
        row.Name = "Row";
        row.AddThemeConstantOverride("separation", 14);
        row.MouseFilter = MouseFilterEnum.Ignore;

        var avatar = MakeAvatar(mine ? "res://assets/art/chat/mc_avatar_v1.png"
                                     : "res://assets/art/chat/her_avatar_v1.png");

        Control bubble = msg.Type == "image"
            ? MakeImageBubble(msg.Image)
            : MakeTextBubble(msg.Text, mine);

        if (mine)
        {
            // 我：气泡靠右，头像在最右
            row.AddChild(MakeHorizontalExpander());
            row.AddChild(bubble);
            row.AddChild(avatar);
        }
        else
        {
            // 她：头像在最左，气泡靠左
            row.AddChild(avatar);
            row.AddChild(bubble);
            row.AddChild(MakeHorizontalExpander());
        }
        return row;
    }

    /// <summary>日期分割线（居中灰字）</summary>
    private static Control MakeDivider(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", DividerColor);
        return label;
    }

    /// <summary>文字气泡（靠近头像的那只角收小 = 气泡"尾巴"）</summary>
    private Control MakeTextBubble(string text, bool mine)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", TextColor);
        label.AddThemeConstantOverride("line_spacing", 6);

        // 量一下文字宽度，给气泡一个合适的"自然宽度"（超宽就换行）
        float textWidth = GetThemeDefaultFont()
            .GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
        label.CustomMinimumSize = new Vector2(Mathf.Min(textWidth + 4f, MaxTextBubbleWidth), 0);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        margin.AddChild(label);

        var bubble = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = mine ? MyBubbleColor : HerBubbleColor };
        style.SetCornerRadiusAll(14);
        if (mine)
            style.CornerRadiusTopRight = 4;
        else
            style.CornerRadiusTopLeft = 4;
        bubble.AddThemeStyleboxOverride("panel", style);
        bubble.AddChild(margin);
        return bubble;
    }

    /// <summary>图片气泡（圆角、按原比例缩放）</summary>
    private Control MakeImageBubble(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        float height = ImageBubbleWidth * tex.GetHeight() / tex.GetWidth();

        var img = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(ImageBubbleWidth, height),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        img.Material = MakeMaskMaterial(ImageBubbleWidth, height, 12);
        return img;
    }

    /// <summary>头像（固定 84×84，圆角方形）</summary>
    private static Control MakeAvatar(string path)
    {
        var avatar = new TextureRect
        {
            Texture = GD.Load<Texture2D>(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(84, 84),
            SizeFlagsVertical = SizeFlags.ShrinkBegin, // 顶部对齐气泡
            MouseFilter = MouseFilterEnum.Ignore,
        };
        avatar.Material = MakeMaskMaterial(84, 84, 16);
        return avatar;
    }

    /// <summary>撑满剩余空间的透明控件（把气泡推到左边或右边）</summary>
    private static Control MakeHorizontalExpander()
    {
        return new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    /// <summary>圆角遮罩材质（把方形图裁成圆角，见 rounded_mask.gdshader）</summary>
    private static ShaderMaterial MakeMaskMaterial(float width, float height, float radius)
    {
        roundedMaskShader ??= GD.Load<Shader>("res://assets/shaders/rounded_mask.gdshader");
        var material = new ShaderMaterial { Shader = roundedMaskShader };
        material.SetShaderParameter("rect_size", new Vector2(width, height));
        material.SetShaderParameter("radius", radius);
        return material;
    }

    // ==================== 数据加载 ====================

    /// <summary>读取 data/chat/{chatId}.json（和对话系统同一套 JSON 流派）</summary>
    private static ChatScriptData LoadChatData(string chatId)
    {
        string path = $"res://data/chat/{chatId}.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[聊天] 找不到聊天文件：{path}");
            return new ChatScriptData();
        }

        string json = FileAccess.GetFileAsString(path);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<ChatScriptData>(json, options);
            data?.EnsureInitialized();
            return data ?? new ChatScriptData();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[聊天] JSON 解析失败 {path}：{e.Message}");
            return new ChatScriptData();
        }
    }
}

// ==================== 聊天数据结构 ====================

/// <summary>一份聊天记录（对应一个 JSON 文件）</summary>
public class ChatScriptData
{
    public string Title { get; set; } = "聊天";
    public List<ChatMessageData> Messages { get; set; }

    public void EnsureInitialized()
    {
        Messages ??= new();
    }
}

/// <summary>一条聊天消息</summary>
public class ChatMessageData
{
    public string Sender { get; set; } = "her"; // her（她）/ me（我）
    public string Type { get; set; } = "text";  // text / image / divider
    public string Text { get; set; } = "";      // 文字内容（divider 用 text 存日期）
    public string Image { get; set; } = "";     // 图片路径（type = image 时用）
}
