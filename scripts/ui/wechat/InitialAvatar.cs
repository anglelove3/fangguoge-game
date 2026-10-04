using Godot;

/// <summary>
/// 首字色块头像：没有头像图的联系人，用微信默认风格的"色块 + 名字首字"
/// （颜色按名字哈希从调色板里挑，同一个人永远同色）
/// 圆角靠 rounded_mask.gdshader 裁出来，和照片头像同一套。
/// </summary>
public partial class InitialAvatar : Control
{
    private static readonly Color[] Palette =
    {
        new Color(0.72f, 0.42f, 0.35f),
        new Color(0.35f, 0.55f, 0.72f),
        new Color(0.42f, 0.62f, 0.45f),
        new Color(0.72f, 0.55f, 0.32f),
        new Color(0.52f, 0.47f, 0.70f),
        new Color(0.36f, 0.60f, 0.62f),
        new Color(0.76f, 0.52f, 0.45f),
        new Color(0.48f, 0.56f, 0.40f),
    };

    private readonly string initial;
    private readonly Color bg;
    private readonly float size;

    public InitialAvatar(string name, float avatarSize)
    {
        size = avatarSize;
        initial = string.IsNullOrEmpty(name) ? "?" : name[..1];
        bg = Palette[Mathf.Abs(name.GetHashCode()) % Palette.Length];
        CustomMinimumSize = new Vector2(avatarSize, avatarSize);
        MouseFilter = MouseFilterEnum.Ignore;
        Material = ChatOverlay.MakeMaskMaterial(avatarSize, avatarSize, 12);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), bg);

        var font = GetThemeDefaultFont();
        int fontSize = (int)(size * 0.44f);
        float textHeight = font.GetAscent(fontSize) + font.GetDescent(fontSize);
        // 基线 y = 垂直居中位置（ascent - descent 的补偿）
        float baselineY = (Size.Y + textHeight) / 2f - font.GetDescent(fontSize);
        DrawString(font, new Vector2(0, baselineY), initial,
                   HorizontalAlignment.Center, Size.X, fontSize, Colors.White);
    }
}
