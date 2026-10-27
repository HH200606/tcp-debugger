using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TcpDebugger;

// 业务逻辑：连接管理、收发、日志渲染
public sealed partial class MainForm
{
    // =========================================================
    // 计时器
    // =========================================================
    private void InitTimers()
    {
        _flushTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();

        _autoSendTimer = new System.Windows.Forms.Timer();
        _autoSendTimer.Tick += (_, _) => DoSend();
    }

    private static IEnumerable<string> GetLocalIPv4()
    {
        var list = new List<string> { "0.0.0.0" };
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(ua.Address.ToString());
                }
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    // =========================================================
    // 客户端：连接 / 断开
    // =========================================================
    private async void DoConnect()
    {
        if (_sessions.Count > 0) return;

        var ipText = txtRemoteIp.Text.Trim();
        if (!IPAddress.TryParse(ipText, out var ip))
        {
            if (!TryResolve(ipText, out ip))
            {
                MessageBox.Show(this, "远端地址不合法：" + ipText, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtRemoteIp.Focus();
                return;
            }
        }

        int port = (int)numRemotePort.Value;
        btnConnect.Enabled = false;
        SetState("正在连接…", Tokens.Warning);
        Tip($"正在连接 {ip}:{port} …");

        try
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(new IPEndPoint(ip, port), cts.Token).ConfigureAwait(true);

            var session = new TcpSession(socket, isAccepted: false);
            AttachSession(session);
            SetState($"已连接 · {port}", Tokens.Success);
            Tip($"已连接到 {session.RemoteEnd}");
        }
        catch (Exception ex)
        {
            string msg = ex is OperationCanceledException ? "连接超时（8 秒）" : ex.Message;
            SetState("连接失败", Tokens.Danger);
            Tip("连接失败：" + msg);
            MessageBox.Show(this, "连接失败：\n" + msg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ApplyMode();
        }
    }

    private static bool TryResolve(string host, out IPAddress ip)
    {
        ip = null;
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
            return ip != null;
        }
        catch { return false; }
    }

    private void DoDisconnect()
    {
        foreach (var s in _sessions.Values.ToList()) s.Close();
        _sessions.Clear();
        RebuildSessionList();
        SetState("未连接", Tokens.TextMuted);
        ApplyMode();
        Tip("已断开连接");
    }

    // =========================================================
    // 服务端：监听 / 停止
    // =========================================================
    private void DoListen()
    {
        if (_listener != null) return;

        var ipText = txtLocalIp.Text.Trim();
        if (!IPAddress.TryParse(ipText, out var ip))
        {
            MessageBox.Show(this, "本地 IP 不合法：" + ipText, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtLocalIp.Focus();
            return;
        }

        int port = (int)numLocalPort.Value;
        try
        {
            _listener = new TcpListener(ip, port);
            _listener.Start(50);
            _ = AcceptLoopAsync(_listener);
            SetState($"监听中 · {port}", Tokens.Success);
            ApplyMode();
            Tip($"已开始监听 {ip}:{port}");
        }
        catch (Exception ex)
        {
            _listener = null;
            MessageBox.Show(this, "监听失败：\n" + ex.Message + "\n\n端口可能已被占用。", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetState("监听失败", Tokens.Danger);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            Socket socket;
            try { socket = await listener.AcceptSocketAsync().ConfigureAwait(false); }
            catch { break; }

            try { socket.NoDelay = true; } catch { }
            var session = new TcpSession(socket, isAccepted: true);
            _ui.Post(_ =>
            {
                AttachSession(session);
                Tip($"新客户端接入 {session.RemoteEnd}");
            }, null);
        }
    }

    private void DoStopListen()
    {
        try { _listener?.Stop(); } catch { }
        _listener = null;
        foreach (var s in _sessions.Values.ToList()) s.Close();
        _sessions.Clear();
        RebuildSessionList();
        SetState("未连接", Tokens.TextMuted);
        ApplyMode();
        Tip("已停止监听");
    }

    // =========================================================
    // 会话管理
    // =========================================================
    private void AttachSession(TcpSession session)
    {
        _sessions[session.Id] = session;
        session.DataReceived += OnSessionData;
        session.Disconnected += OnSessionDisconnected;
        session.Start();
        RebuildSessionList();
        ApplyMode();
    }

    private void OnSessionData(TcpSession session, byte[] data)
    {
        lock (_logLock)
        {
            if (_log.Count > MaxLogEntries) _log.RemoveRange(0, MaxLogEntries / 4);
            _log.Add(new LogEntry(session.RemoteEnd, data, true, _clock.Elapsed));
        }
        lock (_pendingLock) _pending.Append(FormatLine(session.RemoteEnd, data, true, _clock.Elapsed));
    }

    private void OnSessionDisconnected(TcpSession session, string reason)
    {
        _ui.Post(_ =>
        {
            if (!_sessions.Remove(session.Id)) return;
            RebuildSessionList();
            ApplyMode();
            Tip($"连接断开 [{session.RemoteEnd}]：{reason}");
            if (_sessions.Count == 0)
            {
                if (_listener != null) SetState($"监听中 · {numLocalPort.Value}", Tokens.Success);
                else SetState("未连接", Tokens.TextMuted);
            }
        }, null);
    }

    private void RebuildSessionList()
    {
        lvClients.BeginUpdate();
        lvClients.Items.Clear();
        int i = 1;
        foreach (var s in _sessions.Values.OrderBy(x => x.ConnectedAt))
        {
            var item = new ListViewItem(s.RemoteEnd) { Tag = s.Id };
            item.SubItems.Add(i.ToString("D2"));
            lvClients.Items.Add(item);
            i++;
        }
        lvClients.EndUpdate();
        UpdateSessionUi();
    }

    private List<TcpSession> SelectedSessions()
    {
        var result = new List<TcpSession>();
        foreach (ListViewItem it in lvClients.SelectedItems)
            if (it.Tag is string id && _sessions.TryGetValue(id, out var s) && !s.Closed)
                result.Add(s);
        return result;
    }

    private void CloseSelectedSessions()
    {
        var sel = SelectedSessions();
        if (sel.Count == 0) { Tip("请先在左侧选择要断开的连接"); return; }
        foreach (var s in sel) s.Close();
        Tip($"已断开 {sel.Count} 个连接");
    }

    private void SetState(string text, Color color)
    {
        pillState.Set(text, color);
        if (pillState.Parent != null)
            pillState.Left = pillState.Parent.Width - pillState.Width - Tokens.Pad;
        lblStatusConn.Text = "● " + text;
        lblStatusConn.ForeColor = color;
        if (_sessions.Count == 0) lblStatusTxRx.Text = "TX 0 B   RX 0 B";
    }

    private void UpdateTraffic()
    {
        var s = _sessions.Values;
        long tx = s.Sum(x => (long)Interlocked.Read(ref x.BytesSent));
        long rx = s.Sum(x => (long)Interlocked.Read(ref x.BytesReceived));
        lblStatusTxRx.Text = $"TX {FormatBytes(tx)}   RX {FormatBytes(rx)}";
        lblStatusTxRx.ForeColor = (tx + rx) > 0 ? Tokens.TextSecondary : Tokens.TextMuted;
        lblStatusTime.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.0} KB",
        _ => $"{b / 1024.0 / 1024.0:0.00} MB"
    };

    private void Tip(string msg)
    {
        lblStatusMsg.Text = msg;
        lblStatusMsg.ForeColor = Tokens.TextMuted;
    }

    // =========================================================
    // 发送
    // =========================================================
    private void DoSend()
    {
        var targets = SelectedSessions();
        if (targets.Count == 0) targets = _sessions.Values.Where(s => !s.Closed).ToList();

        if (targets.Count == 0)
        {
            Tip("没有可用连接，请先连接或等待客户端接入");
            SetState(segMode.SelectedIndex == 1 ? "等待接入" : "未连接", Tokens.Warning);
            return;
        }

        var text = rtbSend.Text;
        if (chkAppendCrLf.Checked && !chkHexSend.Checked) text += "\r\n";

        byte[] data;
        if (chkHexSend.Checked)
        {
            if (!TryParseHex(text, out data))
            {
                Tip("HEX 格式有误");
                MessageBox.Show(this, "HEX 格式有误。\n示例：01 02 AA FF 或 0102AAFF", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else data = Encoding.UTF8.GetBytes(text);

        if (data.Length == 0) { Tip("发送内容为空"); return; }

        foreach (var s in targets) s.Enqueue(data);

        if (chkShowTx.Checked)
        {
            var who = targets.Count == 1 ? targets[0].RemoteEnd : $"群发({targets.Count})";
            lock (_pendingLock) _pending.Append(FormatLine(who, data, false, _clock.Elapsed));
            lock (_logLock) _log.Add(new LogEntry(who, data, false, _clock.Elapsed));
        }

        Tip($"已发送 {data.Length} 字节 → {targets.Count} 个连接");
    }

    private static bool TryParseHex(string text, out byte[] data)
    {
        data = Array.Empty<byte>();
        var clean = Regex.Replace(text, @"[\s,;:_\-]|0[xX]", "");
        if (clean.Length == 0) return true;
        if (clean.Length % 2 != 0 || !Regex.IsMatch(clean, "^[0-9a-fA-F]+$")) return false;
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        data = bytes;
        return true;
    }

    private void ToggleAutoSend()
    {
        if (chkAutoSend.Checked)
        {
            _autoSendTimer.Interval = (int)numAutoMs.Value;
            _autoSendTimer.Start();
            Tip($"循环发送已开启，间隔 {numAutoMs.Value} ms");
        }
        else
        {
            _autoSendTimer.Stop();
            Tip("循环发送已关闭");
        }
    }

    private void LoadFileToSend()
    {
        using var dlg = new OpenFileDialog { Title = "选择要发送的文件", Filter = "所有文件 (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            rtbSend.Text = chkHexSend.Checked
                ? BitConverter.ToString(bytes).Replace("-", " ")
                : Encoding.UTF8.GetString(bytes);
            Tip($"已载入 {Path.GetFileName(dlg.FileName)}（{bytes.Length} 字节）");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取文件失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateSendCount()
    {
        var t = rtbSend.Text;
        if (chkHexSend.Checked)
        {
            var clean = Regex.Replace(t, @"[\s,;:_\-]", "");
            int n = clean.Length / 2 + clean.Length % 2;
            lblSendCount.Text = $"{n} 字节 HEX";
        }
        else
        {
            int bytes = Encoding.UTF8.GetByteCount(t);
            lblSendCount.Text = $"{bytes} 字节 · {t.Length} 字符";
        }
    }

    private void SaveLog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "导出收发日志",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"tcp-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, GetPlainLog(), new UTF8Encoding(true));
            Tip("日志已导出：" + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =========================================================
    // 接收区渲染
    // =========================================================
    private readonly struct LogEntry
    {
        public readonly string Who;
        public readonly byte[] Data;
        public readonly bool IsRx;
        public readonly TimeSpan At;
        public LogEntry(string who, byte[] data, bool isRx, TimeSpan at)
        { Who = who; Data = data; IsRx = isRx; At = at; }
    }

    private readonly List<LogEntry> _log = new();
    private readonly object _logLock = new();
    private readonly object _pendingLock = new();
    private const int MaxLogEntries = 200000;

    private string FormatLine(string who, byte[] data, bool isRx, TimeSpan at)
    {
        var sb = new StringBuilder(64 + data.Length * 3);
        if (chkShowTime.Checked)
        {
            var now = DateTime.Now;
            sb.Append(now.ToString("HH:mm:ss")).Append('.').Append(now.Millisecond.ToString("D3")).Append("  ");
        }
        sb.Append(isRx ? "RX " : "TX ");
        sb.Append(who.Length > 21 ? who.Substring(0, 21) : who.PadRight(21)).Append("  ");

        if (isRx && chkHexRecv.Checked) sb.Append(ToHex(data));
        else if (!isRx && chkHexSend.Checked) sb.Append(ToHex(data));
        else sb.Append(Encoding.UTF8.GetString(data).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0"));

        sb.Append("  ").Append(data.Length).Append("B\r\n");
        return sb.ToString();
    }

    private static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }

    private void FlushPending()
    {
        string chunk;
        lock (_pendingLock)
        {
            if (_pending.Length == 0) return;
            chunk = _pending.ToString();
            _pending.Clear();
        }
        if (chkPause.Checked) return;

        try
        {
            bool atBottom = IsNearBottom();
            rtbRecv.AppendText(chunk);
            if (rtbRecv.TextLength > 4_000_000)
            {
                rtbRecv.Select(0, 800_000);
                rtbRecv.SelectedText = "";
            }
            if (chkAutoScroll.Checked && atBottom) rtbRecv.ScrollToCaret();
        }
        catch { }

        UpdateTraffic();
        UpdateSendCount();
        UpdateRecvStats();
    }

    private bool IsNearBottom()
    {
        try
        {
            if (rtbRecv.TextLength == 0) return true;
            var pt = rtbRecv.GetPositionFromCharIndex(rtbRecv.TextLength - 1);
            return pt.Y <= rtbRecv.ClientSize.Height + 30;
        }
        catch { return true; }
    }

    private void RebuildReceiveView()
    {
        _pending.Clear();
        List<LogEntry> snapshot;
        lock (_logLock) snapshot = _log.ToList();
        rtbRecv.Clear();
        var sb = new StringBuilder();
        foreach (var e in snapshot) sb.Append(FormatLine(e.Who, e.Data, e.IsRx, e.At));
        rtbRecv.Text = sb.ToString();
        if (chkAutoScroll.Checked) { rtbRecv.SelectionStart = rtbRecv.TextLength; rtbRecv.ScrollToCaret(); }
        UpdateRecvStats();
    }

    private string GetPlainLog()
    {
        List<LogEntry> snapshot;
        lock (_logLock) snapshot = _log.ToList();
        var sb = new StringBuilder();
        foreach (var e in snapshot) sb.Append(FormatLine(e.Who, e.Data, e.IsRx, e.At));
        return sb.ToString();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _flushTimer?.Stop();
        _autoSendTimer?.Stop();
        try { _listener?.Stop(); } catch { }
        foreach (var s in _sessions.Values.ToList()) s.Close();
        base.OnFormClosing(e);
    }
}
