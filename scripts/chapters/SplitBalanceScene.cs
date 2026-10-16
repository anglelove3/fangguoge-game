using Godot;

/// <summary>
/// 第三章《天平》——分屏舞台（第二十四轮 F33）
///
/// 左半屏是他的世界（宿舍书桌，暖黄台灯），右半屏是她的世界（深夜工位，冷蓝屏光），
/// 一条中缝把「一个在上学、一个在工作」隔在同一块夜色里。天平本体悬在中缝上。
///
/// 【状态机】
/// SetStage(1)：失衡加重时两侧同时切到 v2——他的泡面多了一只，她的城市灯灭了大半；
/// 交叉淡切 1.2 秒，像两个人同时没忍住。
///
/// 【塌陷感】SetTilt(0~1)：倾斜越狠，两侧画面越向中缝挤——两个世界一起向中间塌。
///
/// 图：assets/art/backgrounds/balance/（AI 生成水彩，版权与来源已记档）。
/// </summary>
public partial class SplitBalanceScene : Control
{
    private TextureRect leftV1, leftV2, rightV1, rightV2;
    private ColorRect seam;          // 中缝
    private int stage;

    private const string Dir = "res://assets/art/backgrounds/balance/";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        leftV1 = MakeHalf(Dir + "left_v1_dorm.png", true);
        rightV1 = MakeHalf(Dir + "right_v1_office.png", false);
        leftV2 = MakeHalf(Dir + "left_v2_dorm_late.png", true, startAlpha: 0f);
        rightV2 = MakeHalf(Dir + "right_v2_office_late.png", false, startAlpha: 0f);

        // 中缝：一道极细的暖白光，像有人把夜色从中间撕开
        seam = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(1f, 0.96f, 0.86f, 0.22f),
        };
        seam.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        seam.OffsetLeft = -2; seam.OffsetRight = 2;
        seam.OffsetTop = 0; seam.OffsetBottom = GetViewportRect().Size.Y;
        AddChild(seam);
    }

    private TextureRect MakeHalf(string path, bool left, float startAlpha = 1f)
    {
        var rect = new TextureRect
        {
            Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, startAlpha),
        };
        rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (left)
            rect.OffsetRight = -GetViewportRect().Size.X / 2f;   // 只占左半
        else
            rect.OffsetLeft = GetViewportRect().Size.X / 2f;      // 只占右半
        AddChild(rect);
        return rect;
    }

    /// <summary>失衡阶段：1 = 两侧同时切到「更晚」的状态（交叉淡切）</summary>
    public void SetStage(int s, float dur = 1.2f)
    {
        if (s <= stage)
            return;
        stage = s;

        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(leftV2, "modulate:a", 1f, dur).SetTrans(Tween.TransitionType.Sine);
        tw.TweenProperty(rightV2, "modulate:a", 1f, dur).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>塌陷感：k = 0~1（倾斜比例），两侧画面向中缝各挤 18px</summary>
    public void SetTilt(float k)
    {
        k = Mathf.Clamp(k, 0f, 1f);
        float push = k * 18f;
        // 左图右缘向中缝挪，右图左缘向中缝挪（平移整个 TextureRect）
        leftV1.Position = new Vector2(push, 0);
        leftV2.Position = leftV1.Position;
        rightV1.Position = new Vector2(-push, 0);
        rightV2.Position = rightV1.Position;
    }
}
