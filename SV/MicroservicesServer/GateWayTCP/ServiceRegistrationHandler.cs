using ServiceShare.EventBus;
using SharedContracts.LogUltil;
using System.Net.Sockets;

namespace GateWayTCP
{
    /// <summary>
    /// Xử lý event khi có service mới đăng ký.
    /// Tự động tạo TCP connection đến service mới.
    /// </summary>
    public class ServiceRegistrationHandler : IEventHandler<ServiceRegisteredEvent>
    {
        private readonly IMyLogger _logger;

        public string Topic => "service-registry";

        public ServiceRegistrationHandler(IMyLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Được gọi khi Gateway consume ServiceRegisteredEvent từ Kafka.
        /// Routing table được cập nhật TRƯỚC khi TCP connect để tránh miss request.
        /// Thêm delay 500ms để đảm bảo service đã AcceptTcpClientAsync trước khi Gateway ConnectAsync,
        /// giảm race condition giữa Kafka event delivery và TCP accept.
        /// </summary>
        public async Task HandleAsync(ServiceRegisteredEvent @event, CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.Log($"[Gateway] Received ServiceRegisteredEvent: {@event.ServiceName} at {@event.ServiceUrl}");

                string serviceUrl = @event.ServiceUrl;

                // Kiểm tra xem đã có kết nối đến service này chưa
                if (MessageRouter.dicConnectControllerInternal.ContainsKey(serviceUrl))
                {
                    Debug.LogWarning($"[Gateway] Already connected to {serviceUrl}, skipping...");
                    return;
                }

                // Cập nhật routing table TRƯỚC khi kết nối TCP
                foreach (var messageHash in @event.MessageHashes)
                {
                    MessageRouter.dicMessageRouter.TryAdd(messageHash, serviceUrl);
                    Debug.Log($"[Gateway] Mapped message hash {messageHash} -> {serviceUrl}");
                }

                // Chờ ngắn để service chắc chắn đã AcceptTcpClientAsync trước khi Gateway ConnectAsync
                // (giảm race condition giữa Kafka event delivery và TCP accept)
                await Task.Delay(500, cancellationToken);

                // Tạo TCP connection đến service mới
                await ConnectToServiceAsync(serviceUrl, @event.ServiceName, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Error handling ServiceRegisteredEvent: {ex.Message}");
            }
        }

        /// <summary>
        /// Tạo TCP connection đến internal service với retry logic.
        /// Parse "IP:Port" trực tiếp, không dùng hack https://.
        /// </summary>
        private async Task ConnectToServiceAsync(string serviceUrl, string serviceName, CancellationToken cancellationToken)
        {
            try
            {
                // Parse "IP:Port" trực tiếp, không dùng Uri("https://"+serviceUrl)
                var separatorIndex = serviceUrl.LastIndexOf(':');
                if (separatorIndex < 0 || !int.TryParse(serviceUrl[(separatorIndex + 1)..], out int port))
                {
                    Debug.LogError($"[Gateway] Invalid serviceUrl format '{serviceUrl}'. Expected 'IP:Port'.");
                    return;
                }
                string host = serviceUrl[..separatorIndex];
                var tcpClient = new TcpClient();

                // Retry logic với timeout
                int maxRetries = 3;
                int retryDelay = 2000;

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        Debug.Log($"[Gateway] Attempting to connect to {serviceName} at {host}:{port} (Attempt {i + 1}/{maxRetries})");

                        await tcpClient.ConnectAsync(host, port, cancellationToken);

                        var controller = new StreamConnectServiceToGateway(tcpClient);
                        MessageRouter.dicConnectControllerInternal[serviceUrl] = controller;

                        Debug.Log($"[Gateway] Successfully connected to {serviceName} at {serviceUrl}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Gateway] Connection attempt {i + 1} failed: {ex.Message}");

                        if (i < maxRetries - 1)
                        {
                            await Task.Delay(retryDelay, cancellationToken);
                        }
                    }
                }

                Debug.LogError($"[Gateway] Failed to connect to {serviceName} after {maxRetries} attempts");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Fatal error connecting to {serviceName}: {ex.Message}");
            }
        }
    }
}
