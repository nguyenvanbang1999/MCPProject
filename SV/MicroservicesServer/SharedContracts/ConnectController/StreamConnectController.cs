using SharedContracts.LogUltil;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

namespace SharedContracts.ConnectController
{
    public abstract class StreamConnectController<Message>
    {
        /// <summary>
        /// buffer đọc dữ liệu từ stream (dùng tạm thời cho các thao tác nhỏ)
        /// </summary>
        public byte[] buffer;
        /// <summary>
        /// kêt nối TCP
        /// </summary>
        public TcpClient client;
        /// <summary>
        /// Stream data
        /// </summary>
        NetworkStream networkStream;
        /// <summary>
        /// cờ để báo có message cần gửi
        /// </summary>
        private readonly SemaphoreSlim messageSignal = new SemaphoreSlim(0);
        /// <summary>
        /// listMessages cần gửi
        /// </summary>
        public List<Message> listMessages = new List<Message>();

        /// <summary>
        /// Thông tin pending ACK
        /// </summary>
        protected class PendingAck
        {
            public Message Message; // message gốc
            public DateTime SentAtUtc; // thời điểm gửi lần cuối
            public int RetryCount; // số lần retry đã thực hiện
        }
        /// <summary>
        /// Dictionary lưu trữ các message chờ ACK: key: id ACK, value: pending info (protected để subclass truy cập)
        /// </summary>
        protected Dictionary<ushort, PendingAck> dicACKMessage = new Dictionary<ushort, PendingAck>();

        /// <summary> 
        /// luồng đọc
        /// </summary>
        Task readTask = null;
        /// <summary>
        /// luồng ghi
        /// </summary>
        Task writeTask = null;
        /// <summary>
        /// luồng ping kiểm tra kết nối
        /// </summary>
        Task pingTask = null;
        /// <summary>
        /// luồng kiểm tra ACK timeout / retry
        /// </summary>
        Task ackTask = null;

        /// <summary>
        /// Token hủy để kết thúc các vòng lặp đọc/ghi an toàn
        /// </summary>
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        /// <summary>
        /// Cờ đảm bảo sự kiện ngắt kết nối chỉ bắn một lần
        /// </summary>
        private int disconnectedRaised = 0;

        /// <summary>
        /// Guard đảm bảo Close() chỉ chạy một lần dù được gọi từ nhiều luồng/re-entrant
        /// </summary>
        private int _closed = 0;

        /// <summary>
        /// Serialize toàn bộ ghi ra stream theo tuần tự (data + control) để tránh xen kẽ gói
        /// </summary>
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Sự kiện bắn khi mất kết nối (do peer đóng, lỗi IO, hoặc gọi Close)
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// Sự kiện khi nhận được ACK cho message quan trọng
        /// </summary>
        public event Action<ushort, Message> AckReceived;
        /// <summary>
        /// Sự kiện khi ACK timeout (hết retry) cho message quan trọng
        /// </summary>
        public event Action<ushort, Message> AckTimeout;

        // Framing markers
        private const byte FrameData = 0xFD; // frame dữ liệu: [0xFD][len(4)][payload]
        private const byte FrameCtrl = 0xFE; // frame điều khiển: [0xFE][code][optional data]

        // Control codes
        private const byte CtrlPing = 0x01; // ping
        private const byte CtrlAck = 0x02; // ack frame: [0xFE][0x02][ackId(2)] little-endian

        protected int pingStepTime = 1000;

        // Heartbeat state
        private DateTime lastPingAtUtc; // lần cuối nhận ping từ peer (để phát hiện peer không gửi ping)

        // Ngưỡng số chu kỳ ping không nhận được ping từ peer trước khi coi là timeout
        public int missedPongThreshold = 6; // nếu quá N chu kỳ không nhận ping từ peer thì disconnect
        // Alias tên mới cho rõ nghĩa, giữ tương thích với tên cũ
        public int pingMissThreshold
        {
            get => missedPongThreshold;
            set => missedPongThreshold = value;
        }

        private int pingTimeoutMs => pingStepTime * missedPongThreshold; // timeout nếu không nhận ping trong N chu kỳ
        protected bool needLogPing = false;

        // ACK config
        private ushort nextAckId = 1; // sinh tuần tự ackId (>0)
        private readonly int ackCheckIntervalMs = 500; // chu kỳ kiểm tra ACK
        private readonly int ackTimeoutMs = 3000; // thời gian tối đa chờ1 ACK trước khi retry
        private readonly int ackMaxRetry = 3; // số lần retry tối đa

        public StreamConnectController(TcpClient client, int sizeBuffer = 1024)
        {
            buffer = new byte[sizeBuffer];
            this.client = client;
            this.networkStream = client.GetStream();

            var now = DateTime.UtcNow;
            lastPingAtUtc = now;

            readTask = ReadAsync();
            writeTask = WriteAsync();
            pingTask = HeartbeatLoop();
            ackTask = AckMonitorLoop();
        }

        /// <summary>
        /// Đọc đủ count byte từ stream hoặc ném OperationCanceledException khi bị hủy.
        /// Trả về false nếu peer đóng kết nối trước khi đọc đủ.
        /// </summary>
        private async Task<bool> ReadExactAsync(byte[] dst, int offset, int count, CancellationToken token)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read;
                try
                {
                    //Debug.Log($"Check Read Exact: {dst.Length} {offset} {count}");
                    read = await networkStream.ReadAsync(dst, offset + readTotal, count - readTotal, token);
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate
                }
                catch (Exception ex)
                {
                    Debug.Log($"ReadExactAsync exception: {ex}");
                    return false;
                }

                if (read == 0)
                {
                    return false; // remote closed
                }
                readTotal += read;
            }
            return true;
        }

        /// <summary>
        /// Gửi một frame điều khiển đơn giản (không có data thêm)
        /// </summary>
        private async Task SendControlAsync(byte code, CancellationToken token)
        {
            await writeLock.WaitAsync(token);
            try
            {
                // [0xFE][code]
                await networkStream.WriteAsync(new byte[] { FrameCtrl, code }, 0, 2, token);
            }
            finally
            {
                writeLock.Release();
            }
        }

        /// <summary>
        /// Gửi frame ACK: [0xFE][0x02][ackId(2)]
        /// </summary>
        protected async Task SendAckAsync(ushort ackId)
        {
            var ct = cts.Token;
            await writeLock.WaitAsync(ct);
            try
            {
                var ackBytes = BitConverter.GetBytes(ackId); // little-endian2 bytes
                await networkStream.WriteAsync(new byte[] { FrameCtrl, CtrlAck }, 0, 2, ct);
                await networkStream.WriteAsync(ackBytes, 0, ackBytes.Length, ct);
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    Debug.Log($"SendAckAsync exception: {ex}");
            }
            finally
            {
                writeLock.Release();
            }
        }

        /// <summary>
        /// Đọc Message từ stream với framing an toàn
        /// </summary>
        /// <returns></returns>
        async Task ReadAsync()
        {
            var ct = cts.Token;
            try
            {
                var one = new byte[1];
                var lenBuf = new byte[2];
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    bool ok = await ReadExactAsync(one, 0, 1, ct);
                    if (!ok)
                    {
                        Debug.Log("ReadAsync: remote closed while reading frame marker");
                        RaiseDisconnectedOnce();
                        break;
                    }

                    byte marker = one[0];

                    if (marker == FrameCtrl) // Control frame
                    {
                        // Control: [0xFE][code]
                        ok = await ReadExactAsync(one, 0, 1, ct);
                        if (!ok)
                        {
                            Debug.Log("ReadAsync: remote closed while reading control code");
                            RaiseDisconnectedOnce();
                            break;
                        }
                        byte code = one[0];
                        if (code == CtrlPing) // Ping
                        {
                            lastPingAtUtc = DateTime.UtcNow;
                            continue;
                        }
                        else if (code == CtrlAck) // ACK frame
                        {
                            // Đọc ackId (2 bytes)
                            var ackBuf = new byte[2];
                            ok = await ReadExactAsync(ackBuf, 0, 2, ct);
                            if (!ok)
                            {
                                Debug.Log("ReadAsync: remote closed while reading ackId");
                                RaiseDisconnectedOnce();
                                break;
                            }
                            ushort ackId = BitConverter.ToUInt16(ackBuf, 0);
                            Debug.Log($"ACK nhận được: {ackId}");
                            PendingAck pending;
                            bool hasPending;
                            lock (dicACKMessage)
                            {
                                hasPending = dicACKMessage.TryGetValue(ackId, out pending);
                                if (hasPending) dicACKMessage.Remove(ackId);
                            }
                            if (hasPending)
                            {
                                AckReceived?.Invoke(ackId, pending.Message);
                            }
                            else
                            {
                                Debug.Log($"ACK nhận được nhưng không nằm trong pending: {ackId}");
                            }
                            continue;
                        }
                        else
                        {
                            Debug.Log($"ReadAsync: Unknown control code {code}");
                            continue;
                        }
                    }
                    else if (marker == FrameData) // Data frame
                    {
                        // Data: [0xFD][ackID][len(2)][payload]
                        bool okACK = await ReadExactAsync(lenBuf, 0, 2, ct);
                        if (!okACK)
                        {
                            Debug.Log("ReadAsync: remote closed while reading data ackId");
                            RaiseDisconnectedOnce();
                            break;
                        }
                        ushort ackId = BitConverter.ToUInt16(lenBuf, 0);
                        bool okLen = await ReadExactAsync(lenBuf, 0, 2, ct);
                        if (!okLen)
                        {
                            Debug.Log("ReadAsync: remote closed while reading data length");
                            RaiseDisconnectedOnce();
                            break;
                        }
                        ushort length = BitConverter.ToUInt16(lenBuf, 0);
                        if (length < 0)
                        {
                            Debug.Log($"ReadAsync: invalid negative length {length}");
                            RaiseDisconnectedOnce();
                            break;
                        }
                        if (length == 0)
                        {
                            // empty payload (ignore)
                            continue;
                        }

                        var data = new byte[length];
                        bool okData = await ReadExactAsync(data, 0, length, ct);
                        if (!okData)
                        {
                            Debug.Log("ReadAsync: remote closed while reading data payload");
                            RaiseDisconnectedOnce();
                            break;
                        }

                        try
                        {
                            var msg = DeserializeMessage(data);
                            Debug.Log("Nhận được data: " + this.GetType());
                            OnReadMessage(msg,ackId);
                            if (ackId != 0)
                            {
                                // Gửi ACK nếu message có ackId

                                await SendAckAsync(ackId);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.Log($"Deserialize/OnReadMessage exception: {ex}");
                        }
                    }
                    else
                    {
                        // Byte không hợp lệ theo giao thức => ngắt
                        Debug.Log($"ReadAsync: Unknown frame marker {marker}");
                        RaiseDisconnectedOnce();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // bị hủy khi đóng
            }
            catch (Exception ex)
            {
                Debug.Log($"ReadAsync exception: {ex}");
                RaiseDisconnectedOnce();
            }
            finally
            {
                Debug.Log("ReadAsync: End");
                // Đảm bảo luôn bắn sự kiện khi vòng lặp đọc kết thúc
                RaiseDisconnectedOnce();
            }
        }
        /// <summary>
        /// ghi Message ra stream theo frame [0xFD][len(4)][payload]
        /// </summary>
        /// <returns></returns>
        async Task WriteAsync()
        {
            var ct = cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await messageSignal.WaitAsync(ct); }
                    catch (OperationCanceledException) { break; }

                    List<Message> pendingMessages = null;

                    // Copy ra một batch message để xử lý ngoài lock
                    lock (listMessages)
                    {
                        if (listMessages.Count == 0) continue;
                        pendingMessages = new List<Message>(listMessages);
                        listMessages.Clear();
                    }

                    // Gửi các message ra stream (ngoài lock)
                    foreach (var msg in pendingMessages)
                    {
                        try
                        {
                            var payload = SerializeMessage(msg);
                            if (payload == null) continue;
                            var lenBytes = BitConverter.GetBytes((ushort)payload.Length);
                            ushort ackId = GetAckId(msg);
                            var dataAck = BitConverter.GetBytes(ackId);
                            await writeLock.WaitAsync(ct);
                            try
                            {
                                // Frame: [0xFD][ackID][len(4)][payload]
                                await networkStream.WriteAsync(new byte[] { FrameData }, 0, 1, ct);
                                await networkStream.WriteAsync(dataAck, 0, 2, ct);
                                await networkStream.WriteAsync(lenBytes, 0, lenBytes.Length, ct);
                                await networkStream.WriteAsync(payload, 0, payload.Length, ct);
                            }
                            finally { writeLock.Release(); }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Debug.Log($"WriteAsync exception: {ex}");
                            RaiseDisconnectedOnce();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"WriteAsync outer exception: {ex}");
                RaiseDisconnectedOnce();
            }
        }
        // Lấy ackId đã gán cho message (nếu có), hoặc 0 nếu không dùng ACK
        ushort GetAckId(Message message)
        {
            lock (dicACKMessage)
            {
                foreach (var item in dicACKMessage)
                {
                    if (item.Value.Message.Equals(message))
                    {
                        return item.Key;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Heartbeat: cả hai phía gửi ping định kỳ; nếu quá pingTimeoutMs (N chu kỳ) không nhận ping từ peer thì ngắt.
        /// </summary>
        /// <returns></returns>
        async Task HeartbeatLoop()
        {
            var ct = cts.Token;
            while (!ct.IsCancellationRequested)
            {
                try { await SendControlAsync(CtrlPing, ct); if (needLogPing) Debug.Log(">= PING"); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Debug.Log($"Heartbeat exception: {ex}"); RaiseDisconnectedOnce(); break; }

                // Kiểm tra timeout (không nhận ping từ peer)
                try { await Task.Delay(pingStepTime, ct); } catch (OperationCanceledException) { break; }

                var now = DateTime.UtcNow;
                if ((now - lastPingAtUtc).TotalMilliseconds > pingTimeoutMs)
                {
                    Debug.Log("Heartbeat: ping timeout from peer");
                    RaiseDisconnectedOnce();
                    break;
                }
            }
        }

        /// <summary>
        /// Vòng lặp kiểm tra timeout / retry cho ACK
        /// TODO: Bổ sung cơ chế exponential backoff / cấu hình riêng từng loại message.
        /// TODO: Bổ sung cơ chế hủy pending khi disconnect đối tác.
        /// TODO: Ghi log thống kê tỉ lệ mất ACK.
        /// </summary>
        private async Task AckMonitorLoop()
        {
            var ct = cts.Token;
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(ackCheckIntervalMs, ct); }
                catch (OperationCanceledException) { break; }

                var now = DateTime.UtcNow;
                var toRetry = new List<ushort>();
                var toTimeout = new List<ushort>();

                lock (dicACKMessage)
                {
                    foreach (var kv in dicACKMessage)
                    {
                        var ackId = kv.Key;
                        var pending = kv.Value;
                        var elapsedMs = (now - pending.SentAtUtc).TotalMilliseconds;
                        if (elapsedMs > ackTimeoutMs)
                        {
                            if (pending.RetryCount < ackMaxRetry)
                            {
                                toRetry.Add(ackId);
                            }
                            else
                            {
                                toTimeout.Add(ackId);
                            }
                        }
                    }
                }

                // Retry gửi lại message
                foreach (var ackId in toRetry)
                {
                    PendingAck pending;
                    lock (dicACKMessage)
                    {
                        if (!dicACKMessage.TryGetValue(ackId, out pending)) continue;
                        pending.RetryCount++;
                        pending.SentAtUtc = now;
                    }
                    // Đưa lại message vào queue gửi
                    lock (listMessages) { listMessages.Add(pending.Message); }
                    messageSignal.Release();
                    Debug.Log($"ACK retry {ackId} (retry #{pending.RetryCount})");
                }

                // Timeout ACK
                foreach (var ackId in toTimeout)
                {
                    PendingAck pending;
                    lock (dicACKMessage)
                    {
                        if (!dicACKMessage.TryGetValue(ackId, out pending)) continue;
                        dicACKMessage.Remove(ackId);
                    }
                    AckTimeout?.Invoke(ackId, pending.Message);
                    Debug.Log($"ACK timeout {ackId} after {pending.RetryCount} retries");
                }
            }
        }

        /// <summary>
        /// Xử lý dữ liệu đọc được từ stream (message đã giải mã).
        /// NOTE: Subclass có thể gửi ACK bằng cách gọi SendAckAsync nếu message có trường ackId.
        /// </summary>
        protected virtual void OnReadMessage(Message data,ushort ackId) { }

        /// <summary>
        /// Serialize Message thành mảng byte để gửi qua stream
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        protected abstract byte[] SerializeMessage(Message message);

        /// <summary>
        /// Deserialize mảng byte nhận được từ stream thành Message
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        protected abstract Message DeserializeMessage(byte[] data);

        /// <summary>
        /// Gửi Message qua stream. 
        /// </summary>
        public void SendMessage(Message message, bool needACK)
        {

            if (needACK)
            {
                // Lock chung cho việc cấp ackId + ghi vào dictionary để tránh 2 luồng gọi SendMessage
                // đồng thời (VD: Gateway relay message từ nhiều kết nối client) cấp trùng ackId.
                lock (dicACKMessage)
                {
                    ushort ackId = AllocateAckId();
                    dicACKMessage[ackId] = new PendingAck
                    {
                        Message = message,
                        SentAtUtc = DateTime.UtcNow,
                        RetryCount = 0
                    };
                }
            }

            lock (listMessages) { listMessages.Add(message); }
            messageSignal.Release();
        }

        private ushort AllocateAckId()
        {
            // Đơn giản: tăng tuần tự và bỏ qua0 (0 nghĩa không dùng ACK)
            // ack id bắt đầu từ 1 
            if (nextAckId == 0 || nextAckId == ushort.MaxValue) nextAckId = 1; else nextAckId++;
            return nextAckId;
        }




        public void Close()
        {
            // Guard: chỉ chạy một lần dù gọi từ nhiều luồng hoặc re-entrant qua Disconnected event
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try
            {
                cts.Cancel();
                try { messageSignal.Release(); } catch { }
                try { networkStream?.Close(); } catch { }
                try { client?.Close(); } catch { }
            }
            finally
            {
                // Không dispose Task vì trong .NET 8 Task không giữ unmanaged resource.
                // Dispose khi task còn running (đang gọi Close() qua Disconnected event)
                // sẽ ném InvalidOperationException.
                writeLock?.Dispose();
                RaiseDisconnectedOnce();
            }
        }

        private void RaiseDisconnectedOnce()
        {
            if (Interlocked.Exchange(ref disconnectedRaised, 1) == 0)
            {
                try
                {
                    Debug.Log("Raising Disconnected event");
                    Disconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.Log($"Disconnected event handler threw: {ex}");
                }
            }
        }
    }
}
