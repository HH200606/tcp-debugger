namespace TcpDebugger;

/// <summary>
/// 设计令牌（Design Tokens）
/// 依据 ui-ux-pro-max 生成的设计系统：风格 = Industrial / Utility，调色板 #97 Calm Complementary。
/// 所有界面颜色、字体、间距统一从这里取，避免散落硬编码。
/// </summary>
internal static class Tokens
{
    // ---------- 色彩：主色阶 ----------
    public static readonly Color Primary = Color.FromArgb(0x45, 0x44, 0xA7);      // #4544A7
    public static readonly Color PrimaryHover = Color.FromArgb(0x3A, 0x39, 0x92);
    public static readonly Color PrimaryActive = Color.FromArgb(0x2E, 0x2D, 0x7A);
    public static readonly Color Accent = Color.FromArgb(0x35, 0x33, 0xCC);       // #3533CC
    public static readonly Color PrimarySoft = Color.FromArgb(0xEE, 0xEE, 0xF9);  // 主色淡底

    // ---------- 色彩：表面与边框 ----------
    public static readonly Color Bg = Color.FromArgb(0xF6, 0xF6, 0xF9);           // #F6F6F9
    public static readonly Color Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);      // #FFFFFF
    public static readonly Color SurfaceAlt = Color.FromArgb(0xFA, 0xFA, 0xFC);
    public static readonly Color Border = Color.FromArgb(0xD7, 0xD7, 0xEA);       // #D7D7EA
    public static readonly Color BorderStrong = Color.FromArgb(0xB9, 0xB9, 0xD4);
    public static readonly Color GridLine = Color.FromArgb(0xE9, 0xE9, 0xF2);

    // ---------- 色彩：文字 ----------
    public static readonly Color Text = Color.FromArgb(0x16, 0x18, 0x1D);         // #16181D  对比度 16.46:1
    public static readonly Color TextSecondary = Color.FromArgb(0x55, 0x58, 0x66);
    public static readonly Color TextMuted = Color.FromArgb(0x8A, 0x8D, 0x9E);
    public static readonly Color TextOnPrimary = Color.White;

    // ---------- 色彩：语义状态（工业工具风：克制、低饱和）----------
    public static readonly Color Success = Color.FromArgb(0x1E, 0x7F, 0x4F);
    public static readonly Color SuccessSoft = Color.FromArgb(0xE6, 0xF4, 0xEC);
    public static readonly Color Danger = Color.FromArgb(0xC0, 0x2D, 0x3E);
    public static readonly Color DangerSoft = Color.FromArgb(0xFC, 0xEA, 0xEC);
    public static readonly Color Warning = Color.FromArgb(0xB4, 0x7A, 0x0A);
    public static readonly Color WarningSoft = Color.FromArgb(0xFD, 0xF3, 0xDF);
    public static readonly Color Info = Color.FromArgb(0x2A, 0x5F, 0xA8);

    // ---------- 色彩：终端 / 日志区 ----------
    public static readonly Color TerminalBg = Color.FromArgb(0x15, 0x17, 0x20);
    public static readonly Color TerminalText = Color.FromArgb(0xD8, 0xDD, 0xE8);
    public static readonly Color TerminalRx = Color.FromArgb(0x6E, 0xE7, 0xB7);   // 接收：薄荷绿
    public static readonly Color TerminalTx = Color.FromArgb(0xFF, 0xC9, 0x7A);   // 发送：琥珀
    public static readonly Color TerminalMeta = Color.FromArgb(0x74, 0x7A, 0x8C); // 时间戳
    public static readonly Color TerminalPeer = Color.FromArgb(0x8F, 0x9B, 0xD8); // 对端地址

    // ---------- 字体 ----------
    // 中文环境按 Geist 的调性（中性几何无衬线）就近取 Segoe UI Variable / 微软雅黑
    private static readonly string UiFamily = PickUiFamily();
    private static readonly string MonoFamily = PickMonoFamily();

    public static readonly Font FontBase = new(UiFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FontLabel = new(UiFamily, 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FontLabelStrong = new(UiFamily, 8.25F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font FontSection = new(UiFamily, 8F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font FontTitle = new(UiFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font FontButton = new(UiFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FontMono = new(MonoFamily, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FontMonoSmall = new(MonoFamily, 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FontMonoTiny = new(MonoFamily, 7.75F, FontStyle.Regular, GraphicsUnit.Point);

    // ---------- 间距 / 圆角 ----------
    public const int Gap = 8;
    public const int GapSm = 6;
    public const int Pad = 12;
    public const int Radius = 6;
    public const int ControlH = 32;
    public const int RowH = 32;

    private static string PickUiFamily()
    {
        // 优先 Segoe UI Variable（Win11 现代几何无衬线），其次 Segoe UI
        foreach (var f in new[] { "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI" })
            if (IsInstalled(f)) return f;
        return "Microsoft YaHei UI";
    }

    private static string PickMonoFamily()
    {
        foreach (var f in new[] { "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas" })
            if (IsInstalled(f)) return f;
        return "Courier New";
    }

    private static bool IsInstalled(string name)
    {
        try
        {
            using var ff = new FontFamily(name);
            return ff.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>按比例混合两个颜色，t=0 返回 a，t=1 返回 b。</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static float _scale = 1f;
    private static bool _scaleInit;

    /// <summary>当前 DPI 相对 96 的缩放比（125% → 1.25）。</summary>
    public static float Scale
    {
        get
        {
            if (!_scaleInit)
            {
                try
                {
                    using var g = Graphics.FromHwnd(IntPtr.Zero);
                    _scale = g.DpiX / 96f;
                }
                catch { _scale = 1f; }
                if (_scale < 0.5f || _scale > 4f) _scale = 1f;
                _scaleInit = true;
            }
            return _scale;
        }
    }

    /// <summary>把逻辑像素换算成设备像素。</summary>
    public static int Dp(int logical) => (int)Math.Round(logical * Scale);

    /// <summary>根据当前字体行高推算一个控件/行所需高度，最小不低于逻辑值。</summary>
    public static int RowHeight(int logical, Font font = null)
        => Math.Max(Dp(logical), TextRenderer.MeasureText("国Ag", font ?? FontBase).Height + Dp(10));

    /// <summary>相对亮度（WCAG）</summary>
    public static double Luminance(Color c)
    {
        double Ch(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary>对比度（WCAG AA 正文要求 >= 4.5）</summary>
    public static double Contrast(Color fg, Color bg)
    {
        double l1 = Luminance(fg), l2 = Luminance(bg);
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }
}
