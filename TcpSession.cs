using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpDebugger;

/// <summary>一条 TCP 连接（客户端主动连接 或 服务端接受的连接）。</summary>
internal sealed class TcpSession : IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private int _sendRunning;

    public string Id { get; }
    public string RemoteEnd { get; }
    public bool IsAccepted { get; }
    public DateTime ConnectedAt { get; } = DateTime.Now;
    public long BytesSent;
    public long BytesReceived;
    public volatile bool Closed;

    /// <summary>收到数据（在后台线程触发）</summary>
    public event Action<TcpSession, byte[]> DataReceived;
    /// <summary>连接关闭（在后台线程触发）</summary>
    public event Action<TcpSession, string> Disconnected;

    public TcpSession(Socket socket, bool isAccepted)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        IsAccepted = isAccepted;
        try
        {
            var rep = socket.RemoteEndPoint as IPEndPoint;
            RemoteEnd = rep == null ? "?" : rep.Address.Equals(IPAddress.Any) || rep.Address.Equals(IPAddress.IPv6Any)
                ? rep.ToString()
                : rep.ToString();
            if (isAccepted)
            {
                var local = socket.LocalEndPoint as IPEndPoint;
                RemoteEnd = rep == null ? "?" : rep.ToString();
                LocalEnd = local == null ? "?" : local.ToString();
            }
            else
            {
                var local = socket.LocalEndPoint as IPEndPoint;
                LocalEnd = local == null ? "?" : local.ToString();
            }
        }
        catch
        {
            RemoteEnd = "?";
            LocalEnd = "?";
        }
        Id = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
    }

    public string LocalEnd { get; }

    public void Start()
    {
        _ = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cts.Token).ConfigureAwait(false);
                if (n <= 0)
                {
                    RaiseDisconnected("对端关闭连接");
                    return;
                }
                Interlocked.Add(ref BytesReceived, n);
                var data = new byte[n];
                Buffer.BlockCopy(buffer, 0, data, 0, n);
                DataReceived?.Invoke(this, data);
            }
        }
        catch (OperationCanceledException)
        {
            RaiseDisconnected("本地主动断开");
        }
        catch (Exception ex)
        {
            RaiseDisconnected(ex.InnerException?.Message ?? ex.Message);
        }
    }

    private int _disconnectRaised;

    private void RaiseDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _disconnectRaised, 1) == 1) return;
        Closed = true;
        Disconnected?.Invoke(this, reason);
    }

    /// <summary>把数据放进发送队列（自动分包，按顺序发送）。</summary>
    public void Enqueue(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        _sendQueue.Enqueue(data);
        if (Interlocked.Exchange(ref _sendRunning, 1) == 0)
            _ = SendLoopAsync();
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (!Closed && _sendQueue.TryDequeue(out var data))
            {
                await _stream.WriteAsync(data, _cts.Token).ConfigureAwait(false);
                await _stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                Interlocked.Add(ref BytesSent, data.Length);
            }
        }
        catch (Exception ex)
        {
            RaiseDisconnected("发送失败: " + (ex.InnerException?.Message ?? ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _sendRunning, 0);
            if (!Closed && !_sendQueue.IsEmpty)
            {
                if (Interlocked.Exchange(ref _sendRunning, 1) == 0)
                    _ = SendLoopAsync();
            }
        }
    }

    public void Close()
    {
        if (Closed) return;
        Closed = true;
        try { _cts.Cancel(); } catch { }
        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket.Close(); } catch { }
        try { _stream.Dispose(); } catch { }
        RaiseDisconnected("本地主动断开");
    }

    public void Dispose() => Close();
}
