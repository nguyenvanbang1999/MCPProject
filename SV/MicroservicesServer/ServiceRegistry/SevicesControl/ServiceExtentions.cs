using SharedContracts.ConnectController;
using SharedContracts.Messages;
using System.Net;
using System.Net.Sockets;

namespace ServiceRegistry.SevicesControl
{
    /// <summary>
    /// Provides service registration and discovery functionality for microservices.
    /// Services register their message handlers, allowing the gateway to route messages correctly.
    /// </summary>
    public class ServiceExtentions
    {
        /// <summary>
        /// TCP listener for accepting gateway connections.
        /// </summary>
        public static TcpListener? tcpListener;

        /// <summary>
        /// Connection to the gateway for sending messages to clients.
        /// </summary>
        public static StreamConnectControllerMessage? connectToGateway;

        /// <summary>
        /// Chu kỳ heartbeat re-register. ServiceRegistry giữ toàn bộ state (dicMessageRouter/dicServices)
        /// trong RAM — nếu Registry restart, mọi đăng ký biến mất và Gateway không biết service này tồn tại
        /// nữa cho tới khi có một lần đăng ký mới. Vì service nghiệp vụ thường sống lâu hơn Registry (có thể
        /// restart độc lập), ta chủ động POST /register lại định kỳ để Registry tự phục hồi, thay vì phải
        /// restart thủ công tất cả service theo đúng thứ tự.
        /// </summary>
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// HttpClient riêng cho vòng lặp heartbeat — sống suốt vòng đời service, tách khỏi HttpClient
        /// dùng cho lần đăng ký đầu (được dispose ngay sau khi RegisterServiceAsync trả về).
        /// </summary>
        private static readonly HttpClient heartbeatClient = new HttpClient();

        /// <summary>
        /// Registers this service with the Service Registry.
        /// Discovers all message types the service can handle and sends registration info to registry.
        /// Also starts a TCP listener for the gateway to connect.
        /// </summary>
        /// <param name="urlRegistry">URL of the Service Registry (e.g., "https://localhost:5000")</param>
        /// <param name="urlAuth">URL of this service (e.g., "https://localhost:5001")</param>
        /// <param name="serviceName">Tên của service (e.g., "AuthService", "GameService")</param>
        /// <remarks>
        /// Trước đây là "async void" — exception khi đăng ký sẽ mất hút vì không ai await được.
        /// Giờ là "async Task" để Program.cs await, thấy được lỗi, và có retry khi POST /register thất bại.
        /// </remarks>
        public async static Task RegisterServiceAsync(string urlRegistry, string urlAuth, string serviceName = "UnknownService")
        {
            using var httpClient = new HttpClient();

            // Chỉ đăng ký các message type mà service này THỰC SỰ có handler (kế thừa MessageReviceController<>).
            // Trước đây dùng GetAllTypeMessage() nên MỌI MessageBase trong các assembly đã nạp đều bị đăng ký,
            // khiến LevelService/ResourceService (có tham chiếu AuthService.Contracts) chiếm nhầm route của CMLogin/SMLogin.
            List<Type> listTypeMessage = MessageUtil.MapTypeMessageHandle.Keys.ToList();
            List<uint> listHashMessage = new List<uint>();

            // Step 1: Khởi động TcpListener trước khi POST /register
            // để Gateway có thể kết nối ngay sau khi nhận Kafka event
            tcpListener = new TcpListener(IPAddress.Any, 0);
            tcpListener.Start();
            var localEndpoint = (IPEndPoint)tcpListener.LocalEndpoint;

            // Step 2: Bắt đầu chờ Gateway kết nối (non-blocking)
            _ = WaitGateWayConnect();

            foreach (var type in listTypeMessage)
            {
                uint hash = MessageUtil.GetMessageTypeId(type);
                listHashMessage.Add(hash);
            }

            Console.WriteLine($"[{serviceName}] Registering {listTypeMessage.Count} handled message type(s): " +
                (listTypeMessage.Count > 0 ? string.Join(", ", listTypeMessage.Select(t => t.Name)) : "(none — push-only service)"));

            var localIp = Dns.GetHostAddresses(Dns.GetHostName())
                 .First(a => a.AddressFamily == AddressFamily.InterNetwork);

            var serviceInfo = new ServiceRegistry.RegistryMessageRouterInfo(
                urlService: localIp.ToString() + ":" + localEndpoint.Port,
                hashMessage: listHashMessage,
                serviceName: serviceName
            );

            // Step 3: POST /register — lúc này TcpListener đã sẵn sàng accept.
            // Retry vì Registry có thể chưa kịp sẵn sàng dù Aspire đã WaitFor (network/DNS transient).
            await TryRegisterOnceAsync(httpClient, urlRegistry, serviceInfo, serviceName, maxAttempts: 3);

            // Step 4: Heartbeat re-register định kỳ suốt vòng đời service (xem HeartbeatInterval ở trên).
            // Idempotent phía Registry: /register dùng AddOrUpdate + luôn publish, và Gateway bỏ qua
            // (chỉ log) nếu đã có kết nối tới service này — nên gọi lại định kỳ không tạo kết nối trùng.
            _ = HeartbeatRegisterLoopAsync(urlRegistry, serviceInfo, serviceName);
        }

        /// <summary>
        /// POST /register một lần, retry tối đa <paramref name="maxAttempts"/> lần với backoff tuyến tính.
        /// </summary>
        /// <returns>true nếu đăng ký thành công, false nếu hết lượt retry.</returns>
        private static async Task<bool> TryRegisterOnceAsync(
            HttpClient httpClient, string urlRegistry, RegistryMessageRouterInfo serviceInfo, string serviceName, int maxAttempts)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await httpClient.PostAsJsonAsync(urlRegistry + "/register", serviceInfo);
                    response.EnsureSuccessStatusCode();
                    Console.WriteLine($"[{serviceName}] Service registered successfully at {serviceInfo.urlService}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{serviceName}] Register attempt {attempt}/{maxAttempts} failed: {ex.Message}");
                    if (attempt == maxAttempts)
                    {
                        Console.WriteLine($"[{serviceName}] Giving up registering after {maxAttempts} attempts. " +
                            "Heartbeat loop sẽ tiếp tục thử lại định kỳ.");
                        return false;
                    }
                    await Task.Delay(2000 * attempt);
                }
            }
            return false;
        }

        /// <summary>
        /// Vòng lặp nền: định kỳ POST /register lại để Registry tự phục hồi state nếu đã restart.
        /// Chạy vô thời hạn theo vòng đời process (fire-and-forget từ RegisterServiceAsync).
        /// </summary>
        private static async Task HeartbeatRegisterLoopAsync(string urlRegistry, RegistryMessageRouterInfo serviceInfo, string serviceName)
        {
            while (true)
            {
                await Task.Delay(HeartbeatInterval);
                try
                {
                    var response = await heartbeatClient.PostAsJsonAsync(urlRegistry + "/register", serviceInfo);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[{serviceName}] Heartbeat re-register trả về {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    // Registry có thể tạm thời không sẵn sàng (đang restart) — bỏ qua, thử lại ở chu kỳ sau.
                    Console.WriteLine($"[{serviceName}] Heartbeat re-register thất bại (thử lại sau {HeartbeatInterval.TotalSeconds}s): {ex.Message}");
                }
            }
        }
        /// <summary>
        /// Waits indefinitely for the Gateway to connect, then sets connectToGateway.
        /// Automatically re-waits when the Gateway disconnects (e.g., restart).
        /// No timeout — accepts the connection whenever the Gateway is ready, regardless of startup order.
        /// </summary>
        public static async Task WaitGateWayConnect()
        {
            if (tcpListener == null)
            {
                throw new InvalidOperationException("TcpListener is not initialized. Call RegisterService first.");
            }

            Console.WriteLine("[WaitGateWayConnect] Waiting for Gateway to connect...");
            while (true)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    var controller = new StreamConnectControllerMessage(client);

                    // Re-wait automatically when Gateway disconnects (e.g., Gateway restarts)
                    controller.Disconnected += OnGatewayDisconnected;

                    connectToGateway = controller;
                    Console.WriteLine("[WaitGateWayConnect] Gateway connected successfully.");
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // TcpListener was stopped (app shutting down) — exit cleanly
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WaitGateWayConnect] Accept error: {ex.Message}. Retrying in 2s...");
                    await Task.Delay(2000);
                }
            }
        }

        /// <summary>
        /// Called when the Gateway connection drops. Clears connectToGateway and restarts the accept loop.
        /// </summary>
        private static void OnGatewayDisconnected()
        {
            Console.WriteLine("[WaitGateWayConnect] Gateway disconnected. Re-waiting for reconnect...");
            connectToGateway = null;
            _ = WaitGateWayConnect();
        }
    }
}
