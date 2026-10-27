using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TcpDebugger;

// =====================================================================
// 通用绘制辅助
// =====================================================================
internal static class Draw
{
    public static void RoundRect(Graphics g, Rectangle r, int radius, Color fill, Color? border = null, float borderW = 1f)
    {
        using var path = RoundPath(r, radius);
        if (fill.A > 0)
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
        }
        if (border.HasValue && borderW > 0)
        {
            using var p = new Pen(border.Value, borderW);
            g.DrawPath(p, path);
        }
    }

    public static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(0, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        if (d <= 1)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>开启高质量抗锯齿</summary>
    public static void Quality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }
}

// =====================================================================
// 扁平按钮：主按钮 / 次按钮 / 危险按钮 / 幽灵按钮
// =====================================================================
internal enum ButtonKind { Primary, Secondary, Danger, Ghost, Success }

internal sealed class FlatButton : Button
{
    private bool _hover, _down;

    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
    /// <summary>可选的左侧圆点指示色（如连接状态）</summary>
    public Color? DotColor { get; set; }

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        Kind = kind;
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Tokens.FontButton;
        Height = Tokens.ControlH;
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Color.Transparent;
        Padding = Padding.Empty;   // 文字由 OnPaint 自绘，避免基类内边距挤占
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _hover = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);

        var (fill, fore, border) = Resolve();
        if (!Enabled)
        {
            fill = Tokens.Mix(fill, Tokens.Bg, 0.55);
            fore = Tokens.TextMuted;
            border = Tokens.Border;
        }
        else if (_down)
        {
            fill = Tokens.Mix(fill, Color.Black, 0.16);
        }
        else if (_hover)
        {
            fill = Kind == ButtonKind.Secondary || Kind == ButtonKind.Ghost
                ? Tokens.Mix(fill, Tokens.Primary, 0.10)
                : Tokens.Mix(fill, Color.Black, 0.08);
        }

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Draw.RoundRect(g, r, Tokens.Radius, fill, border);

        // 焦点环（UX-005：可见焦点指示）
        if (Focused && Enabled)
        {
            var fr = Rectangle.Inflate(r, -2, -2);
            using var pen = new Pen(Tokens.Mix(Tokens.Accent, fill, 0.35), 1.4f) { DashStyle = DashStyle.Dot };
            using var path = Draw.RoundPath(fr, Tokens.Radius - 1);
            g.DrawPath(pen, path);
        }

        // 左侧状态点
        int leftPad = 0;
        if (DotColor.HasValue)
        {
            int cy = Height / 2, cx = 11;
            using var b = new SolidBrush(DotColor.Value);
            g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
            leftPad = 12;
        }

        // 文字按实测尺寸居中绘制（不用 HorizontalCenter，避免控件区与文本区 DPI 不一致时溢出被裁）
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(leftPad, 0, Width - leftPad, Height), fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private (Color fill, Color fore, Color border) Resolve() => Kind switch
    {
        ButtonKind.Primary => (Tokens.Primary, Tokens.TextOnPrimary, Tokens.PrimaryActive),
        ButtonKind.Success => (Tokens.Success, Color.White, Tokens.Mix(Tokens.Success, Color.Black, 0.2)),
        ButtonKind.Danger => (Tokens.DangerSoft, Tokens.Danger, Tokens.Mix(Tokens.DangerSoft, Tokens.Danger, 0.35)),
        ButtonKind.Ghost => (Color.Transparent, Tokens.TextSecondary, Tokens.Border),
        _ => (Tokens.Surface, Tokens.Text, Tokens.Border),
    };

    public static FlatButton Make(string text, ButtonKind kind, int width)
        => new(text, kind) { Width = width };

    /// <summary>按文字实测宽度自适应（含左右内边距），避免中文在高 DPI 下被截断。</summary>
    /// <summary>按文字实测宽度自适应。句柄创建后（DPI 已确定）会再校正一次。</summary>
    public FlatButton Fit(int padLogical = 30, int minLogical = 56)
    {
        _padLogical = padLogical;
        _minLogical = minLogical;
        ApplyFit();
        return this;
    }

    private int _padLogical = 30, _minLogical = 56;

    /// <summary>AutoSize 父容器按此尺寸分配空间，避免按基类 PreferredSize 算得过小。</summary>
    public override Size GetPreferredSize(Size proposedSize)
        => new(Math.Max(Width, MinimumSize.Width), Math.Max(Height, MinimumSize.Height));

    private void ApplyFit()
    {
        if (string.IsNullOrEmpty(Text)) return;
        // 优先用控件自身的 Graphics（真实 DPI）；未创建时退回静态测量
        int textW;
        if (IsHandleCreated)
        {
            using var g = CreateGraphics();
            textW = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }
        else
        {
            textW = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }
        Width = Math.Max(textW + Tokens.Dp(_padLogical), Tokens.Dp(_minLogical));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyFit();   // 句柄就绪后按真实 DPI 重新校正宽度
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ApplyFit();
    }
}

// =====================================================================
// 状态胶囊：● 已连接 / 监听中
// =====================================================================
internal sealed class StatusPill : Control
{
    private Color _dot = Tokens.TextMuted;
    private string _label = "未连接";

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Font = Tokens.FontLabelStrong;
        Height = 24;
        BackColor = Color.Transparent;
    }

    public void Set(string label, Color dot)
    {
        _label = label;
        _dot = dot;
        using var g = CreateGraphics();
        int w = TextRenderer.MeasureText(g, label, Font).Width + 30;
        Width = Math.Max(72, w);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var bg = Tokens.Mix(Tokens.Surface, _dot, 0.10);
        Draw.RoundRect(g, r, Height / 2, bg, Tokens.Mix(Tokens.Surface, _dot, 0.34));

        bool running = _dot != Tokens.TextMuted;
        int cx = 12, cy = Height / 2;
        if (running)
        {
            // 呼吸光环
            using var halo = new SolidBrush(Color.FromArgb(45, _dot));
            g.FillEllipse(halo, cx - 6, cy - 6, 12, 12);
        }
        using var dot = new SolidBrush(_dot);
        g.FillEllipse(dot, cx - 3, cy - 3, 6, 6);

        TextRenderer.DrawText(g, _label, Font, new Rectangle(22, 0, Width - 26, Height),
            Tokens.Mix(_dot, Tokens.Text, 0.25),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

// =====================================================================
// 分段控件：TCP 客户端 / 服务端
// =====================================================================
internal sealed class SegmentedControl : Control
{
    private int _selected;
    private int _hoverIndex = -1;
    public string[] Items { get; set; } = Array.Empty<string>();
    public event EventHandler SelectedChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= Items.Length || value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SegmentedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Font = Tokens.FontButton;
        Height = Tokens.ControlH;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        TabStop = true;
    }

    private Rectangle SegmentRect(int i)
    {
        int segW = (Width - 4) / Math.Max(1, Items.Length);
        return new Rectangle(2 + i * segW, 2, segW, Height - 4);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = -1;
        for (int i = 0; i < Items.Length; i++)
            if (SegmentRect(i).Contains(e.Location)) { idx = i; break; }
        if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < Items.Length; i++)
            if (SegmentRect(i).Contains(e.Location)) { SelectedIndex = i; break; }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left && _selected > 0) SelectedIndex = _selected - 1;
        else if (e.KeyCode == Keys.Right && _selected < Items.Length - 1) SelectedIndex = _selected + 1;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);

        // 轨道
        Draw.RoundRect(g, new Rectangle(0, 0, Width - 1, Height - 1), Tokens.Radius + 1,
            Tokens.Mix(Tokens.Bg, Tokens.Border, 0.45), Tokens.Border);

        for (int i = 0; i < Items.Length; i++)
        {
            var r = SegmentRect(i);
            bool sel = i == _selected;

            if (sel)
            {
                Draw.RoundRect(g, r, Tokens.Radius, Tokens.Surface, Tokens.BorderStrong);
                // 选中态底部主色下划线
                using var accent = new SolidBrush(Tokens.Primary);
                g.FillRectangle(accent, r.X + 8, r.Bottom - 3, r.Width - 16, 2);
            }
            else if (i == _hoverIndex)
            {
                Draw.RoundRect(g, r, Tokens.Radius, Color.FromArgb(120, Tokens.Surface));
            }

            TextRenderer.DrawText(g, Items[i], Font, r,
                sel ? Tokens.Primary : Tokens.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (Focused)
        {
            using var pen = new Pen(Tokens.Accent) { DashStyle = DashStyle.Dot };
            using var path = Draw.RoundPath(new Rectangle(1, 1, Width - 3, Height - 3), Tokens.Radius);
            g.DrawPath(pen, path);
        }
    }
}

// =====================================================================
// 复选框：方形勾选 + 主色填充
// =====================================================================
internal sealed class FlatCheckBox : CheckBox
{
    private bool _hover;

    public FlatCheckBox(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Font = Tokens.FontLabel;
        AutoSize = false;
        Height = 26;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = Tokens.TextSecondary;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = TextRenderer.MeasureText(Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Tokens.Dp(26);
        return new Size(w, Tokens.Dp(26));
    }

    /// <summary>按文字实际长度设置宽度，避免文字被截断。</summary>
    public FlatCheckBox Fit()
    {
        // 构造期句柄可能还没创建，用不依赖 Graphics 的重载
        int textW = TextRenderer.MeasureText(Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        Width = textW + Tokens.Dp(26);
        // 高度也一并固定：放在 FlowLayoutPanel 里时若高度被压小，勾选框会跑到文字下方
        Height = Tokens.Dp(26);
        return this;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);

        var bg = Parent?.BackColor ?? Tokens.Surface;
        // 底面（避免透明穿透时出现黑底）
        using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);

        int box = 15, cy = Height / 2;
        var boxRect = new Rectangle(1, cy - box / 2, box, box);

        if (Checked)
        {
            Draw.RoundRect(g, boxRect, 4, Tokens.Primary, Tokens.PrimaryActive);
            // 勾
            using var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(boxRect.X + 3.6f, boxRect.Y + 7.6f),
                new PointF(boxRect.X + 6.4f, boxRect.Y + 10.4f),
                new PointF(boxRect.X + 11.4f, boxRect.Y + 4.6f),
            });
        }
        else
        {
            var fill = _hover ? Tokens.Mix(Tokens.Surface, Tokens.Primary, 0.06) : Tokens.Surface;
            Draw.RoundRect(g, boxRect, 4, fill, _hover ? Tokens.Primary : Tokens.BorderStrong, 1.2f);
        }

        var fore = Enabled ? (Checked ? Tokens.Text : Tokens.TextSecondary) : Tokens.TextMuted;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(box + 7, 0, Width - box - 8, Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (Focused)
        {
            using var pen = new Pen(Tokens.Accent) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 0, cy - 9, Width - 1, 18);
        }
    }
}

// =====================================================================
// 输入框容器：标签 + 输入框，带边框与聚焦高亮
// =====================================================================
internal sealed class LabeledInput : Panel
{
    private readonly Label _label;
    private Control _input;
    private bool _focused;

    public Control Input => _input;

    public LabeledInput(string label, Control input, int labelWidth = 62)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Height = Tokens.ControlH;
        _input = input;
        _label = new Label
        {
            Text = label,
            Font = Tokens.FontLabel,
            ForeColor = Tokens.TextMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };

        input.Font = Tokens.FontMono;
        input.Margin = new Padding(0);

        Controls.Add(input);
        Controls.Add(_label);

        input.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        input.LostFocus += (_, _) => { _focused = false; Invalidate(); };
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_label == null || _input == null) return;

        // 标签固定在左侧一小块区域（右侧留一个竖分隔线），输入控件占其余空间。
        // 输入控件高度自适应容器：不留死高度，否则容器会被压成"最小高度"。
        int lw = PadLeft;
        int ih = Math.Min(_input.PreferredSize.Height, Height - Tokens.Dp(2));
        if (ih < Tokens.Dp(16)) ih = Math.Max(Tokens.Dp(16), Height);
        _label.SetBounds(0, 0, lw - Tokens.Dp(6), Height);
        _input.SetBounds(lw, (Height - ih) / 2, Math.Max(10, Width - lw - Tokens.Dp(4)), ih);
    }

    /// <summary>标签区宽度。两字中文标签需要 ~46px，故按 48 逻辑像素取。</summary>
    private int PadLeft => Tokens.Dp(48);

    /// <summary>上报首选高度，避免 TableLayoutPanel 按内部最小高度把整行压扁。</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = proposedSize.Width > 0 ? proposedSize.Width : Tokens.Dp(180);
        return new Size(w, Tokens.Dp(Tokens.ControlH));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var border = _focused ? Tokens.Primary : Tokens.BorderStrong;
        Draw.RoundRect(g, r, Tokens.Dp(Tokens.Radius), Tokens.Surface, border, _focused ? 1.5f : 1f);

        // 标签与输入区之间的竖分隔
        using var sep = new Pen(Tokens.Border);
        g.DrawLine(sep, PadLeft - Tokens.Dp(4), Tokens.Dp(6), PadLeft - Tokens.Dp(4), Height - Tokens.Dp(6));
    }
}

// =====================================================================
// 卡片容器：标题 + 内容，白底细边框
// =====================================================================
internal sealed class Card : Panel
{
    private readonly string _title;
    private readonly string _hint;
    private readonly bool _accentBar;

    public Panel Body { get; }

    public Card(string title, string hint = "", bool accentBar = true)
    {
        _title = title;
        _hint = hint;
        _accentBar = accentBar;
        HeaderHeight = Tokens.Dp(36);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Tokens.Surface;
        Dock = DockStyle.Fill;
        AutoSize = false;
        Padding = new Padding(Tokens.Dp(Tokens.Pad), HeaderHeight + Tokens.Dp(6), Tokens.Dp(Tokens.Pad), Tokens.Dp(Tokens.Pad));

        Body = new Panel { Dock = DockStyle.Fill, BackColor = Tokens.Surface };
        Controls.Add(Body);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        // 始终填满父容器，避免在各类面板中被 AutoSize 压成默认尺寸
        Dock = DockStyle.Fill;
    }

    public int HeaderHeight { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Quality(g);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Draw.RoundRect(g, r, Tokens.Dp(Tokens.Radius + 2), Tokens.Surface, Tokens.Border);

        int hh = HeaderHeight;
        using (var b = new SolidBrush(Tokens.SurfaceAlt))
        using (var path = Draw.RoundPath(new Rectangle(1, 1, Width - 3, hh), Tokens.Dp(Tokens.Radius + 1)))
            g.FillPath(b, path);
        using (var pen = new Pen(Tokens.Border))
            g.DrawLine(pen, 1, hh, Width - 2, hh);

        int tx = Tokens.Dp(Tokens.Pad);
        if (_accentBar)
        {
            using var accent = new SolidBrush(Tokens.Primary);
            g.FillRectangle(accent, Tokens.Dp(Tokens.Pad), hh / 2 - Tokens.Dp(6), Tokens.Dp(3), Tokens.Dp(11));
            tx += Tokens.Dp(10);
        }

        TextRenderer.DrawText(g, _title, Tokens.FontSection,
            new Rectangle(tx, 0, Width - tx - Tokens.Dp(8), hh), Tokens.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

        if (!string.IsNullOrEmpty(_hint))
        {
            var sz = TextRenderer.MeasureText(g, _hint, Tokens.FontMonoTiny);
            TextRenderer.DrawText(g, _hint, Tokens.FontMonoTiny,
                new Rectangle(Width - sz.Width - Tokens.Dp(Tokens.Pad) - 2, 0, sz.Width + 2, hh),
                Tokens.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }
    }
}
