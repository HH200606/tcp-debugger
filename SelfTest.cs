using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpDebugger;

/// <summary>内置自测：验证 TcpSession 的收发、HEX 解析、多连接群发等核心链路。</summary>
internal static class SelfTest
{
    private static readonly StringBuilder Report = new();
    private static int _failed;

    private static void Ok(string name, string detail = "")
        => Report.AppendLine($"  [PASS] {name}{(detail.Length > 0 ? "  —  " + detail : "")}");

    private static void Fail(string name, string detail)
    {
        _failed++;
        Report.AppendLine($"  [FAIL] {name}  —  {detail}");
    }

    public static int Run()
    {
        Report.AppendLine("===== TCP 调试助手 自测 =====");
        Report.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        TestClientEcho();
        TestHexParse();
        TestServerMultiClient();

        Report.AppendLine();
        Report.AppendLine(_failed == 0 ? "结果: 全部通过" : $"结果: {_failed} 项失败");

        var path = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
        File.WriteAllText(path, Report.ToString(), new UTF8Encoding(true));
        Console.WriteLine(Report.ToString());
        return _failed == 0 ? 0 : 1;
    }

    // ---------- 1. 客户端会话：连接 -> 发送 -> 接收回显 ----------
    private static void TestClientEcho()
    {
        Report.AppendLine();
        Report.AppendLine("[1] 客户端会话收发（连接回显服务器）");

        // 启动一个临时回显服务
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var cli = await listener.AcceptTcpClientAsync();
            var st = cli.GetStream();
            var buf = new byte[8192];
            while (true)
            {
                int n = await st.ReadAsync(buf);
                if (n == 0) break;
                var reply = Encoding.UTF8.GetBytes("ECHO:" + Encoding.UTF8.GetString(buf, 0, n));
                await st.WriteAsync(reply);
            }
        });

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(new IPEndPoint(IPAddress.Loopback, port));

            var session = new TcpSession(socket, isAccepted: false);
            var received = new TaskCompletionSource<string>();
            var sb = new StringBuilder();
            session.DataReceived += (_, data) =>
            {
                sb.Append(Encoding.UTF8.GetString(data));
                if (sb.ToString().Contains("ECHO:")) received.TrySetResult(sb.ToString());
            };
            session.Disconnected += (_, reason) => received.TrySetResult("<断开:" + reason + ">");
            session.Start();

            session.Enqueue(Encoding.UTF8.GetBytes("hello-tcp"));

            if (received.Task.Wait(4000))
            {
                var got = received.Task.Result;
                if (got.Contains("ECHO:hello-tcp"))
                    Ok("发送后收到回显", got.Trim());
                else
                    Fail("发送后收到回显", "实际收到: " + got);
            }
            else
            {
                Fail("发送后收到回显", "4 秒内未收到数据");
            }

            if (Interlocked.Read(ref session.BytesSent) == 9 && Interlocked.Read(ref session.BytesReceived) > 0)
                Ok("字节计数", $"发送 {session.BytesSent} / 接收 {session.BytesReceived}");
            else
                Fail("字节计数", $"发送 {session.BytesSent} / 接收 {session.BytesReceived}");

            session.Close();
        }
        catch (Exception ex)
        {
            Fail("客户端会话收发", ex.Message);
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { serverTask.Wait(1000); } catch { }
        }
    }

    // ---------- 2. HEX 解析 ----------
    private static void TestHexParse()
    {
        Report.AppendLine();
        Report.AppendLine("[2] HEX 解析");

        var m = typeof(MainForm).GetMethod("TryParseHex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (m == null) { Fail("TryParseHex 反射获取", "方法不存在"); return; }

        bool Try(string input, out byte[] data)
        {
            var args = new object[] { input, null };
            var ok = (bool)m.Invoke(null, args);
            data = (byte[])args[1];
            return ok;
        }

        if (Try("01 02 AA FF", out var d1) && d1.Length == 4 && d1[2] == 0xAA)
            Ok("空格分隔 HEX", BitConverter.ToString(d1));
        else Fail("空格分隔 HEX", "解析结果不符");

        if (Try("0102AAFF", out var d2) && d2.Length == 4 && d2[3] == 0xFF)
            Ok("连续 HEX", BitConverter.ToString(d2));
        else Fail("连续 HEX", "解析结果不符");

        if (!Try("0G12", out _)) Ok("非法字符被拒绝", "0G12");
        else Fail("非法字符被拒绝", "应当返回 false");

        if (!Try("ABC", out _)) Ok("奇数长度被拒绝", "ABC");
        else Fail("奇数长度被拒绝", "应当返回 false");
    }

    // ---------- 3. 服务端模式：多客户端接入 + 群发 ----------
    private static void TestServerMultiClient()
    {
        Report.AppendLine();
        Report.AppendLine("[3] 服务端模式多客户端接入");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var sessions = new List<TcpSession>();
        var acceptTask = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                var s = await listener.AcceptSocketAsync();
                var sess = new TcpSession(s, isAccepted: true);
                lock (sessions) sessions.Add(sess);
                sess.Start();
            }
        });

        var clients = new List<TcpClient>();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var c = new TcpClient();
                c.Connect(IPAddress.Loopback, port);
                clients.Add(c);
            }
            acceptTask.Wait(4000);
            Thread.Sleep(300);

            lock (sessions)
            {
                if (sessions.Count == 2) Ok("接受 2 个客户端", string.Join(", ", sessions.Select(x => x.RemoteEnd)));
                else Fail("接受 2 个客户端", $"实际 {sessions.Count}");
            }

            // 群发
            var payload = Encoding.UTF8.GetBytes("broadcast");
            lock (sessions) foreach (var s in sessions) s.Enqueue(payload);
            Thread.Sleep(600);

            int got = 0;
            foreach (var c in clients)
            {
                c.ReceiveTimeout = 2000;
                var buf = new byte[256];
                try
                {
                    int n = c.GetStream().Read(buf, 0, buf.Length);
                    if (n > 0 && Encoding.UTF8.GetString(buf, 0, n) == "broadcast") got++;
                }
                catch { }
            }
            if (got == 2) Ok("群发到全部客户端", "2/2 收到 broadcast");
            else Fail("群发到全部客户端", $"{got}/2 收到");
        }
        catch (Exception ex)
        {
            Fail("服务端多客户端", ex.Message);
        }
        finally
        {
            foreach (var c in clients) c.Close();
            lock (sessions) foreach (var s in sessions) s.Close();
            try { listener.Stop(); } catch { }
        }
    }
}
