using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TcpDebugger;

public sealed partial class MainForm : Form
{
    // ---------- 顶部：模式与连接参数 ----------
    private SegmentedControl segMode;
    private TextBox txtRemoteIp;
    private NumericUpDown numRemotePort;
    private FlatButton btnConnect;
    private FlatButton btnDisconnect;
    private FlatButton btnListen;
    private FlatButton btnStopListen;
    private TextBox txtLocalIp;
    private NumericUpDown numLocalPort;
    private StatusPill pillState;
    private SplitContainer _ioSplit;
    private Label lblRemoteGroup;
    private Label lblLocalGroup;

    // ---------- 左侧：连接列表 ----------
    private ListView lvClients;
    private Label lblSessionCount;
    private FlatButton btnCloseSel;

    // ---------- 中间：接收区 ----------
    private RichTextBox rtbRecv;
    private FlatCheckBox chkHexRecv;
    private FlatCheckBox chkShowTime;
    private FlatCheckBox chkShowTx;
    private FlatCheckBox chkAutoScroll;
    private FlatCheckBox chkPause;
    private FlatButton btnClearRecv;
    private FlatButton btnSaveRecv;
    private Label lblRecvStats;

    // ---------- 底部：发送区 ----------
    private RichTextBox rtbSend;
    private FlatCheckBox chkHexSend;
    private FlatCheckBox chkAppendCrLf;
    private FlatCheckBox chkAutoSend;
    private NumericUpDown numAutoMs;
    private FlatButton btnSend;
    private FlatButton btnClearSend;
    private FlatButton btnLoadFile;
    private Label lblSendCount;

    // ---------- 状态栏 ----------
    private Panel statusBar;
    private Label lblStatusConn;
    private Label lblStatusTxRx;
    private Label lblStatusMsg;
    private Label lblStatusTime;

    // ---------- 运行状态 ----------
    private TcpListener _listener;
    private SynchronizationContext _ui;
    private readonly Dictionary<string, TcpSession> _sessions = new();
    private readonly StringBuilder _pending = new();
    private System.Windows.Forms.Timer _flushTimer;
    private System.Windows.Forms.Timer _autoSendTimer;
    private readonly Stopwatch _clock = new();

    public MainForm()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _clock.Start();
        BuildUi();
        InitTimers();
        ApplyMode();
    }

    // =========================================================
    // 窗口
    // =========================================================
    protected override void OnLoad(EventArgs e)
    {
        // 窗口尺寸在 OnLoad 设置，避免与停靠布局的初始计算冲突
        base.OnLoad(e);
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

        // 严格按工作区留白：减去窗口边框，保证整窗可见（本机可用区 1536x912）
        int chromeW = Width - ClientSize.Width;   // 左右边框
        int chromeH = Height - ClientSize.Height; // 标题栏 + 下边框

        int maxClientW = wa.Width - chromeW - 40;
        int maxClientH = wa.Height - chromeH - 40;

        int w = Math.Min(1180, maxClientW);
        int h = Math.Min(760, maxClientH);

        MinimumSize = new Size(Math.Min(820, w + chromeW), Math.Min(520, h + chromeH));
        MaximumSize = new Size(wa.Width, wa.Height);
        ClientSize = new Size(w, h);

        Location = new Point(
            wa.Left + Math.Max(0, (wa.Width - (w + chromeW)) / 2),
            wa.Top + Math.Max(0, (wa.Height - (h + chromeH)) / 2));

        PerformLayout();
        ForceDeepLayout(this);
        ApplySplitLayout();
        txtRemoteIp.Focus();

        if (Environment.GetEnvironmentVariable("TCPDBG_DUMP") == "1")
        {
            try
            {
                var sb = new StringBuilder();
                void Walk(Control c, int d)
                {
                    sb.Append(new string(' ', d * 2)).Append(c.GetType().Name)
                      .Append(" ").Append(c.Bounds).Append(" vis=").Append(c.Visible)
                      .Append(" txt=").Append((c.Text ?? "").Replace("\r", " ").Replace("\n", " "))
                      .AppendLine();
                    if (d > 14) return;
                    foreach (Control ch in c.Controls) Walk(ch, d + 1);
                }
                Walk(this, 0);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-dump.txt"), sb.ToString());
            }
            catch { }
        }
    }

    /// <summary>
    /// 深度触发一次布局。WinForms 在 OnLoad 中改 ClientSize 后，
    /// 嵌套的 Dock=Fill 子控件偶尔不会重新计算，需要逐层 PerformLayout。
    /// </summary>
    private static void ForceDeepLayout(Control root)
    {
        void Walk(Control c, int depth)
        {
            if (depth > 8) return;
            c.PerformLayout();
            foreach (Control child in c.Controls) Walk(child, depth + 1);
        }
        Walk(root, 0);
        root.Refresh();
    }

    /// <summary>分隔面板的最小尺寸与初始比例必须在控件获得真实尺寸后设置。</summary>
    private void ApplySplitLayout()
    {
        if (_ioSplit == null) return;
        try
        {
            int h = _ioSplit.Height;
            if (h < 240) return;
            _ioSplit.Panel1MinSize = 120;
            _ioSplit.Panel2MinSize = 120;
            int target = (int)(h * 0.62);
            target = Math.Max(_ioSplit.Panel1MinSize,
                     Math.Min(h - _ioSplit.Panel2MinSize - _ioSplit.SplitterWidth, target));
            _ioSplit.SplitterDistance = target;

            // 关键：SplitterDistance 变化后，两个 Panel 内的 Fill 子控件不会自动重排，
            // 必须显式填充，否则 Card 会停留在 Panel 的默认尺寸。
            _ioSplit.Panel1.PerformLayout();
            _ioSplit.Panel2.PerformLayout();
            foreach (Control c in _ioSplit.Panel1.Controls)
            {
                c.Dock = DockStyle.None;
                c.Bounds = _ioSplit.Panel1.ClientRectangle;
                c.Dock = DockStyle.Fill;
                ForceDeepLayout(c);
            }
            foreach (Control c in _ioSplit.Panel2.Controls)
            {
                c.Dock = DockStyle.None;
                c.Bounds = _ioSplit.Panel2.ClientRectangle;
                c.Dock = DockStyle.Fill;
                ForceDeepLayout(c);
            }
        }
        catch { }
    }

    // =========================================================
    // 界面构建
    // =========================================================
    private void BuildUi()
    {
        Text = "TCP 调试助手";
        StartPosition = FormStartPosition.CenterScreen;
        Font = Tokens.FontBase;
        BackColor = Tokens.Bg;
        DoubleBuffered = true;
        KeyPreview = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Tokens.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.Dp(58)));   // 品牌栏
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.Dp(64)));   // 参数条
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));              // 主体
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.Dp(28)));   // 状态栏

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildParamBar(), 0, 1);
        root.Controls.Add(BuildBody(), 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);
        Controls.Add(root);

        KeyDown += OnGlobalKey;
    }

    // ---------------- 品牌栏 ----------------
    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.Surface,
            Padding = new Padding(Tokens.Pad, 0, Tokens.Pad, 0),
            Margin = new Padding(0)
        };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Draw.Quality(g);

            // 左侧品牌标记：方块 + 折线（网络节点意象）
            var mark = new Rectangle(0, 15, 28, 28);
            Draw.RoundRect(g, mark, 7, Tokens.Primary);
            using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 1.6f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
            {
                // 三点 + 连线
                g.DrawLine(pen, mark.X + 8, mark.Y + 18, mark.X + 14, mark.Y + 9);
                g.DrawLine(pen, mark.X + 14, mark.Y + 9, mark.X + 20, mark.Y + 18);
                using var b = new SolidBrush(Color.White);
                g.FillEllipse(b, mark.X + 5.5f, mark.Y + 15.5f, 5, 5);
                g.FillEllipse(b, mark.X + 11.5f, mark.Y + 6.5f, 5, 5);
                g.FillEllipse(b, mark.X + 17.5f, mark.Y + 15.5f, 5, 5);
            }

            TextRenderer.DrawText(g, "TCP 调试助手", Tokens.FontTitle,
                new Rectangle(38, 12, 300, 22), Tokens.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "TCP DEBUGGER", Tokens.FontMonoTiny,
                new Rectangle(39, 33, 300, 16), Tokens.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        };

        // 右侧状态胶囊
        pillState = new StatusPill { Anchor = AnchorStyles.Right | AnchorStyles.Top, Top = 17 };
        pillState.Set("未连接", Tokens.TextMuted);
        header.Controls.Add(pillState);
        header.Resize += (_, _) => pillState.Left = header.Width - pillState.Width - Tokens.Pad;

        // 底部分隔线
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Tokens.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        return header;
    }

    // ---------------- 参数条 ----------------
    private Control BuildParamBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.SurfaceAlt,
            Padding = new Padding(Tokens.Pad, 0, Tokens.Pad, 0),
            Margin = new Padding(0)
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Tokens.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };

        // 参数条：外层行高 70dp，内层 TableLayoutPanel 用上下 Padding 挤出居中的内容区，
        // 子控件 Dock=Fill 填满内容区（32dp 高），既不被拉伸也不会错位。
        const int barH = 64;                       // 与根布局参数条行高保持一致
        int vPad = Tokens.Dp((barH - Tokens.ControlH) / 2);
        var flow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 13,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0),
            AutoSize = false
        };
        flow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // 列：模式 | 分隔 | "远端" | IP(拉伸) | 端口 | 连接 | 断开 | 分隔 | "本地" | IP(拉伸) | 端口 | 监听 | 停止
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));

        // 模式分段控件：高度与其他控件保持一致（垂直居中）
        segMode = new SegmentedControl
        {
            Items = new[] { "客户端", "服务端" },
            Dock = DockStyle.Fill,
            Margin = new Padding(0, vPad, 0, vPad)
        };
        segMode.SelectedChanged += (_, _) => ApplyMode();

        // 远端参数组
        lblRemoteGroup = MakeGroupLabel("远端");
        txtRemoteIp = MakeTextBox("127.0.0.1");
        numRemotePort = MakeNumeric(8080);        var remoteBox = new LabeledInput("IP", txtRemoteIp) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 6, vPad) };
        var remotePortBox = new LabeledInput("端口", numRemotePort) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 6, vPad) };

        btnConnect = new FlatButton("连接", ButtonKind.Primary) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 4, vPad) };
        btnConnect.Click += (_, _) => DoConnect();
        btnDisconnect = new FlatButton("断开", ButtonKind.Danger) { Dock = DockStyle.Fill, Enabled = false, Margin = new Padding(0, vPad, 4, vPad) };
        btnDisconnect.Click += (_, _) => DoDisconnect();

        // 本地参数组
        lblLocalGroup = MakeGroupLabel("本地");
        txtLocalIp = MakeTextBox("0.0.0.0");
        numLocalPort = MakeNumeric(8080);
        var localBox = new LabeledInput("IP", txtLocalIp) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 6, vPad) };
        var localPortBox = new LabeledInput("端口", numLocalPort) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 6, vPad) };

        btnListen = new FlatButton("开始监听", ButtonKind.Success) { Dock = DockStyle.Fill, Margin = new Padding(0, vPad, 4, vPad) };
        btnListen.Click += (_, _) => DoListen();
        btnStopListen = new FlatButton("停止", ButtonKind.Ghost) { Dock = DockStyle.Fill, Enabled = false, Margin = new Padding(0, vPad, 0, vPad) };
        btnStopListen.Click += (_, _) => DoStopListen();

        var items = new Control[]
        {
            segMode, MakeDivider(),
            lblRemoteGroup, remoteBox, remotePortBox, btnConnect, btnDisconnect,
            MakeDivider(),
            lblLocalGroup, localBox, localPortBox, btnListen, btnStopListen
        };
        for (int i = 0; i < items.Length; i++) flow.Controls.Add(items[i], i, 0);

        bar.Controls.Add(flow);
        return bar;
    }

    // 分组标签：单行显示，宽度按文字实测（中文两字在 125% DPI 下要 ~42px）
    private static Label MakeGroupLabel(string text)
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        int w = TextRenderer.MeasureText(g, text, Tokens.FontMonoTiny).Width + 6;
        return new Label
        {
            Text = text,
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            AutoSize = false,
            Width = Math.Max(w, 34),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, Tokens.Dp(16), 0, Tokens.Dp(16))
        };
    }

    private static Panel MakeDivider() => new()
    {
        Width = 1,
        Height = 20,
        BackColor = Tokens.Border,
        Anchor = AnchorStyles.None,
        Margin = new Padding(9, 0, 9, 0)
    };

    private static TextBox MakeTextBox(string text) => new()
    {
        Text = text,
        BorderStyle = BorderStyle.None,
        Font = Tokens.FontMono,
        BackColor = Tokens.Surface,
        ForeColor = Tokens.Text,
        TextAlign = HorizontalAlignment.Left,
        Margin = new Padding(0)
    };

    private static NumericUpDown MakeNumeric(int value) => new()
    {
        Minimum = 1,
        Maximum = 65535,
        Value = value,
        BorderStyle = BorderStyle.None,
        Font = Tokens.FontMono,
        BackColor = Tokens.Surface,
        ForeColor = Tokens.Text,
        TextAlign = HorizontalAlignment.Left,
        Margin = new Padding(0)
    };

    // ---------------- 主体：左连接列表 + 右收发 ----------------
    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Tokens.Bg,
            Padding = new Padding(Tokens.Pad, 10, Tokens.Pad, 6),
            Margin = new Padding(0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 258));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 行必须显式占满，否则单元格会按内容 AutoSize 导致卡片塌缩
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = BuildSessionCard();
        left.Dock = DockStyle.Fill;
        left.Margin = new Padding(0, 0, Tokens.Gap, 0);

        var right = BuildIoArea();
        right.Dock = DockStyle.Fill;
        right.Margin = new Padding(0);

        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);
        return body;
    }

    private Control BuildSessionCard()
    {
        var card = new Card("连接", "SESSIONS");

        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Tokens.Surface
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        lvClients = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
            BackColor = Tokens.Surface,
            ForeColor = Tokens.Text,
            Font = Tokens.FontMonoSmall,
            OwnerDraw = true,
            Margin = new Padding(0)
        };
        lvClients.Columns.Add("远端地址", 158);
        lvClients.Columns.Add("会话", 48);
        lvClients.DrawColumnHeader += OnDrawSessionHeader;
        lvClients.DrawItem += OnDrawSessionItem;
        lvClients.DrawSubItem += OnDrawSessionSubItem;
        lvClients.SelectedIndexChanged += (_, _) => UpdateSessionUi();

        lblSessionCount = new Label
        {
            Dock = DockStyle.Fill,
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            Text = "0 个活动连接",
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Tokens.Surface,
            Padding = new Padding(2, 0, 0, 0)
        };

        btnCloseSel = new FlatButton("断开选中", ButtonKind.Ghost) { Dock = DockStyle.Fill, Enabled = false };
        btnCloseSel.Click += (_, _) => CloseSelectedSessions();

        wrap.Controls.Add(lvClients, 0, 0);
        wrap.Controls.Add(lblSessionCount, 0, 1);
        wrap.Controls.Add(btnCloseSel, 0, 2);
        card.Body.Controls.Add(wrap);
        return card;
    }

    private void OnDrawSessionHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new SolidBrush(Tokens.SurfaceAlt)) g.FillRectangle(b, e.Bounds);
        using (var pen = new Pen(Tokens.Border))
            g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        TextRenderer.DrawText(g, e.Header.Text, Tokens.FontMonoTiny,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            Tokens.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    private static void PaintSessionRow(Graphics g, ListViewItem item, bool isSubItem)
    {
        var bounds = item.Bounds;
        bool selected = item.Selected;

        if (!isSubItem)
        {
            using var bg = new SolidBrush(selected ? Tokens.PrimarySoft : Tokens.Surface);
            g.FillRectangle(bg, bounds);
            if (selected)
            {
                using var accent = new SolidBrush(Tokens.Primary);
                g.FillRectangle(accent, bounds.X, bounds.Y, 3, bounds.Height);
            }
        }
    }

    private void OnDrawSessionItem(object sender, DrawListViewItemEventArgs e)
        => PaintSessionRow(e.Graphics, e.Item, false);

    private void OnDrawSessionSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics;
        bool selected = e.Item.Selected;
        var rb = e.Bounds;
        var text = e.SubItem?.Text ?? "";

        if (e.ColumnIndex == 0)
        {
            TextRenderer.DrawText(g, text, Tokens.FontMonoSmall,
                new Rectangle(rb.X + 6, rb.Y, rb.Width - 8, rb.Height),
                selected ? Tokens.Primary : Tokens.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        else
        {
            // 会话序号做成胶囊
            var pill = new Rectangle(rb.X + 4, rb.Y + (rb.Height - 17) / 2, Math.Min(rb.Width - 10, 34), 17);
            Draw.RoundRect(g, pill, 8,
                selected ? Tokens.Mix(Tokens.PrimarySoft, Tokens.Primary, 0.14) : Tokens.SurfaceAlt,
                Tokens.Border);
            TextRenderer.DrawText(g, text, Tokens.FontMonoTiny, pill,
                selected ? Tokens.Primary : Tokens.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ---------------- 右侧收发区 ----------------
    private Control BuildIoArea()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8,
            BackColor = Tokens.Bg,
            Margin = new Padding(0)
        };
        // PanelMinSize / SplitterDistance 必须等控件有实际尺寸后再设，否则抛异常，
        // 统一放到 OnLoad 处理（见 ApplySplitLayout）。
        _ioSplit = split;

        // ---- 接收卡片 ----
        var recvCard = new Card("接收", "RX");
        var recvGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Tokens.Surface
        };
        recvGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.Dp(36)));
        recvGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 工具栏用 Panel + 手动定位：WinForms 的 Flow/Table 布局在
        // Dock=Fill + Padding 组合下会出现列宽归零、控件重叠，手动排布最可控。
        var recvTools = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.Surface,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        chkShowTime = new FlatCheckBox("时间戳") { Checked = true }.Fit();
        chkShowTx = new FlatCheckBox("含发送") { Checked = true }.Fit();
        chkHexRecv = new FlatCheckBox("HEX").Fit();
        chkAutoScroll = new FlatCheckBox("跟随") { Checked = true }.Fit();
        chkPause = new FlatCheckBox("暂停").Fit();
        chkHexRecv.CheckedChanged += (_, _) => RebuildReceiveView();
        chkShowTime.CheckedChanged += (_, _) => RebuildReceiveView();
        chkShowTx.CheckedChanged += (_, _) => RebuildReceiveView();

        lblRecvStats = new Label
        {
            Text = "",
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Tokens.Surface
        };

        btnClearRecv = new FlatButton("清空", ButtonKind.Ghost) { Size = new Size(76, 26) };
        btnClearRecv.Click += (_, _) => { _log.Clear(); _pending.Clear(); rtbRecv.Clear(); UpdateRecvStats(); };
        btnSaveRecv = new FlatButton("导出", ButtonKind.Ghost) { Size = new Size(76, 26) };
        btnSaveRecv.Click += (_, _) => SaveLog();

        var recvChecks = new[] { chkShowTime, chkShowTx, chkHexRecv, chkAutoScroll, chkPause };
        recvTools.Controls.AddRange(recvChecks);
        recvTools.Controls.Add(lblRecvStats);
        recvTools.Controls.Add(btnClearRecv);
        recvTools.Controls.Add(btnSaveRecv);

        // 手动排布：左起依次摆复选框 → 统计占剩余 → 两个按钮贴右
        recvTools.Layout += (_, _) =>
        {
            int gap = Tokens.Dp(10);
            int y = (recvTools.ClientSize.Height - Tokens.Dp(26)) / 2;
            if (y < 0) y = 0;
            int x = 0;
            foreach (var c in recvChecks)
            {
                c.SetBounds(x, y, c.Width, Tokens.Dp(26));
                x += c.Width + gap;
            }
            int btnW = btnClearRecv.Width, btnW2 = btnSaveRecv.Width;
            int btnY = (recvTools.ClientSize.Height - btnClearRecv.Height) / 2;
            if (btnY < 0) btnY = 0;
            btnSaveRecv.SetBounds(recvTools.ClientSize.Width - btnW2, btnY, btnW2, btnClearRecv.Height);
            btnClearRecv.SetBounds(recvTools.ClientSize.Width - btnW2 - btnW - Tokens.Dp(8), btnY, btnW, btnClearRecv.Height);
            lblRecvStats.SetBounds(x, 0, Math.Max(0, btnClearRecv.Left - x - gap), recvTools.ClientSize.Height);
        };

        rtbRecv = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.TerminalBg,
            ForeColor = Tokens.TerminalText,
            Font = Tokens.FontMono,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            DetectUrls = false,
            Margin = new Padding(0)
        };

        recvGrid.Controls.Add(recvTools, 0, 0);
        recvGrid.Controls.Add(rtbRecv, 0, 1);
        recvCard.Body.Controls.Add(recvGrid);

        // ---- 发送卡片 ----
        var sendCard = new Card("发送", "TX");
        var sendGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Tokens.Surface
        };
        sendGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Tokens.Dp(36)));
        sendGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sendGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // 发送工具栏同样用 Panel + 手动定位
        var sendTools = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.Surface,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        chkHexSend = new FlatCheckBox("HEX").Fit();
        chkAppendCrLf = new FlatCheckBox("加 CRLF").Fit();
        chkAutoSend = new FlatCheckBox("循环").Fit();
        chkAutoSend.CheckedChanged += (_, _) => ToggleAutoSend();
        numAutoMs = new NumericUpDown
        {
            Minimum = 10,
            Maximum = 600000,
            Value = 1000,
            Width = 78,
            Height = 24,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Tokens.FontMonoSmall,
            BackColor = Tokens.Surface,
            ForeColor = Tokens.Text
        };
        var lblMs = new Label
        {
            Text = "ms",
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Tokens.Surface
        };

        lblSendCount = new Label
        {
            Text = "",
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Tokens.Surface
        };

        btnLoadFile = new FlatButton("导入文件", ButtonKind.Ghost) { Size = new Size(112, 26) };
        btnLoadFile.Click += (_, _) => LoadFileToSend();
        btnClearSend = new FlatButton("清空", ButtonKind.Ghost) { Size = new Size(76, 26) };
        btnClearSend.Click += (_, _) => rtbSend.Clear();

        var sendChecks = new Control[] { chkHexSend, chkAppendCrLf, chkAutoSend, numAutoMs, lblMs };
        sendTools.Controls.AddRange(sendChecks);
        sendTools.Controls.Add(lblSendCount);
        sendTools.Controls.Add(btnLoadFile);
        sendTools.Controls.Add(btnClearSend);

        sendTools.Layout += (_, _) =>
        {
            int gap = Tokens.Dp(10);
            int h = Tokens.Dp(26);
            int y = (sendTools.ClientSize.Height - h) / 2;
            if (y < 0) y = 0;
            int x = 0;
            foreach (var c in sendChecks)
            {
                int cw = c == numAutoMs ? numAutoMs.Width
                       : c == lblMs ? Tokens.Dp(24)
                       : c.Width;
                c.SetBounds(x, c == numAutoMs ? y + Tokens.Dp(1) : y, cw, c == numAutoMs ? Tokens.Dp(24) : h);
                x += cw + (c == lblMs ? gap : Tokens.Dp(4));
            }
            int bw = btnClearSend.Width, bw2 = btnLoadFile.Width;
            int by = (sendTools.ClientSize.Height - btnClearSend.Height) / 2;
            if (by < 0) by = 0;
            btnClearSend.SetBounds(sendTools.ClientSize.Width - bw, by, bw, btnClearSend.Height);
            btnLoadFile.SetBounds(sendTools.ClientSize.Width - bw - bw2 - Tokens.Dp(8), by, bw2, btnLoadFile.Height);
            lblSendCount.SetBounds(x + gap, 0, Math.Max(0, btnLoadFile.Left - x - gap * 2), sendTools.ClientSize.Height);
        };

        rtbSend = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = Tokens.FontMono,
            BorderStyle = BorderStyle.None,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            BackColor = Tokens.SurfaceAlt,
            ForeColor = Tokens.Text,
            Text = "Hello TCP!",
            Margin = new Padding(0)
        };
        rtbSend.TextChanged += (_, _) => UpdateSendCount();

        // 发送卡片输入框加内边框（用 Panel 包一层）
        var sendInputHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Tokens.Border };
        sendInputHost.Controls.Add(rtbSend);

        var sendBottom = new Panel { Dock = DockStyle.Fill, BackColor = Tokens.Surface };
        btnSend = new FlatButton("发送  Ctrl+↵", ButtonKind.Primary) { Dock = DockStyle.Right, Width = 128, Height = 32, Margin = new Padding(0) };
        btnSend.Click += (_, _) => DoSend();
        var lblTip = new Label
        {
            Dock = DockStyle.Fill,
            Text = "HEX 示例：01 02 AA FF",
            Font = Tokens.FontMonoTiny,
            ForeColor = Tokens.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Tokens.Surface
        };
        sendBottom.Controls.Add(lblTip);
        sendBottom.Controls.Add(btnSend);

        sendGrid.Controls.Add(sendTools, 0, 0);
        sendGrid.Controls.Add(sendInputHost, 0, 1);
        sendGrid.Controls.Add(sendBottom, 0, 2);
        sendCard.Body.Controls.Add(sendGrid);

        split.Panel1.Controls.Add(recvCard);
        split.Panel2.Controls.Add(sendCard);
        return split;
    }

    // ---------------- 状态栏 ----------------
    private Control BuildStatusBar()
    {
        statusBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Tokens.Surface,
            Padding = new Padding(Tokens.Pad, 0, Tokens.Pad, 0),
            Margin = new Padding(0)
        };
        statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Tokens.Border);
            e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
        };

        lblStatusConn = MakeStatusLabel(Tokens.TextSecondary, 150);
        lblStatusTxRx = MakeStatusLabel(Tokens.TextSecondary, 230);
        lblStatusMsg = MakeStatusLabel(Tokens.TextMuted, 0);
        lblStatusTime = MakeStatusLabel(Tokens.TextMuted, 96, ContentAlignment.MiddleRight);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        flow.Controls.AddRange(new Control[] { lblStatusConn, lblStatusTxRx, lblStatusMsg, lblStatusTime });
        statusBar.Controls.Add(flow);

        statusBar.Resize += (_, _) =>
        {
            int used = lblStatusConn.Width + lblStatusTxRx.Width + lblStatusTime.Width + 30;
            lblStatusMsg.Width = Math.Max(80, statusBar.Width - used - Tokens.Pad * 2);
        };

        return statusBar;
    }

    private static Label MakeStatusLabel(Color fore, int width, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Font = Tokens.FontMonoTiny,
        ForeColor = fore,
        AutoSize = false,
        Width = width,
        Height = 26,
        TextAlign = align,
        BackColor = Color.Transparent,
        Text = ""
    };

    // =========================================================
    // 事件
    // =========================================================
    private void OnGlobalKey(object sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            DoSend();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.L)
        {
            _log.Clear();
            _pending.Clear();
            rtbRecv.Clear();
            e.SuppressKeyPress = true;
        }
    }

    private void ApplyMode()
    {
        bool server = segMode.SelectedIndex == 1;
        bool hasSession = _sessions.Count > 0;

        SetGroupEnabled(lblRemoteGroup, !server, txtRemoteIp, numRemotePort);
        SetGroupEnabled(lblLocalGroup, server, txtLocalIp, numLocalPort);

        btnConnect.Enabled = !server && !hasSession;
        btnListen.Enabled = server && _listener == null;
        btnStopListen.Enabled = server && _listener != null;
        btnDisconnect.Enabled = !server && hasSession;
    }

    private static void SetGroupEnabled(Label label, bool enabled, params Control[] controls)
    {
        foreach (var c in controls) c.Enabled = enabled;
        if (label != null)
            label.ForeColor = enabled ? Tokens.TextMuted : Tokens.Mix(Tokens.TextMuted, Tokens.Surface, 0.45);
    }

    private void UpdateSessionUi()
    {
        int n = _sessions.Count;
        lblSessionCount.Text = $"{n} 个活动连接";
        lblSessionCount.ForeColor = n > 0 ? Tokens.Success : Tokens.TextMuted;
        btnCloseSel.Enabled = lvClients.SelectedItems.Count > 0;
    }

    private void UpdateRecvStats()
    {
        int rx = _log.Count(e => e.IsRx);
        int tx = _log.Count(e => !e.IsRx);
        lblRecvStats.Text = $"{rx} 收 / {tx} 发";
    }
}
