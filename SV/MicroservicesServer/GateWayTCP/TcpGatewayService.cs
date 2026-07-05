using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GateWayTCP
{
    /// <summary>
    /// TCP Gateway service that accepts client connections and routes messages to internal services.
    /// Runs as a background service listening on port 5001 for incoming TCP connections.
    /// </summary>
    public class TcpGatewayService: BackgroundService
    {
        private readonly int _port = 5001;
        private readonly TcpListener _listener;
        private readonly MessageRouter _router;
        private bool _running;
        private readonly IConfiguration _config;
        private readonly ILogger<TcpGatewayService> _logger;
        
        /// <summary>
        /// Initializes a new instance of the TcpGatewayService.
        /// </summary>
        /// <param name="config">Application configuration</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <param name="router">Message router for handling client messages</param>
        public TcpGatewayService(IConfiguration config, ILogger<TcpGatewayService> logger,MessageRouter router)
        {
            _config = config;
            _logger = logger;
            _listener = new TcpListener(IPAddress.Any, _port);
            _router = router;
        }

        /// <summary>
        /// Starts the TCP listener and begins accepting client connections.
        /// Each client connection is handled asynchronously.
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task StartAsync()
        {
            _running = true;
            _listener.Start();
            
            Console.WriteLine($"[TCP] Gateway listening on port {_port}");

            while (_running)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var internalPort = _config.GetValue<int>("ASPIRE__ENDPOINT__TCP_INTERNAL__PORT", 5001);
            var internalHost = _config.GetValue<string>("ASPIRE__ENDPOINT__TCP_INTERNAL__HOST") ?? "localhost";
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[2];
            var endpoint = client.Client.RemoteEndPoint?.ToString();

            Console.WriteLine($"[TCP] Client connected: {endpoint}");

            try
            {
                await ReadExactAsync(buffer, 0, 2, stream, CancellationToken.None);
                ushort sizeDeviceId = BitConverter.ToUInt16(buffer, 0);
                if (sizeDeviceId == 0) return;
                else
                {
                    // lấy id Device của user gửi lên 
                    var  idBuffer = new byte[sizeDeviceId];
                    await ReadExactAsync(idBuffer, 0, sizeDeviceId, stream, CancellationToken.None);
                    string idDeviceStr = Encoding.UTF8.GetString(idBuffer);
                    MessageRouter.dicGuestConnectController.Add(idDeviceStr, new StreamConnectClientGateWay(client));
                    Console.WriteLine($"Client sizeDeviceId: {sizeDeviceId}");
                    Console.WriteLine($"Client DeviceId: {idDeviceStr}");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Error: {ex.Message}");
            }
        }
        private async Task<bool> ReadExactAsync(byte[] dst, int offset, int count,NetworkStream networkStream, CancellationToken token)
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
                    // Warning (không phải Error) vì đây thường là client đóng kết nối giữa chừng — bình thường,
                    // nhưng vẫn log để phân biệt được với lỗi mạng/hạ tầng thật khi cần chẩn đoán.
                    _logger.LogWarning(ex, "ReadExactAsync: lỗi khi đọc từ NetworkStream, coi như kết nối đã đóng");
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
    }
}
