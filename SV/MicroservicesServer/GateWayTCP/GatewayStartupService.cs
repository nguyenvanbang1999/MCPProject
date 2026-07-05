using Microsoft.Extensions.Hosting;
using SharedContracts.LogUltil;
using System.Net.Sockets;

namespace GateWayTCP
{
    /// <summary>
    /// Hosted service ch?y sau khi Gateway ?� fully started.
    /// Load c�c routes hi?n c� t? Registry v� k?t n?i ??n t?t c? services ?ang running.
    /// Thay th? c? ch? Task.Delay th? c�ng � ch?y ?�ng sau khi IHost.StartAsync() ho�n t?t.
    /// </summary>
    public class GatewayStartupService : IHostedLifecycleService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly TcpGatewayService _tcpGatewayService;

        public GatewayStartupService(IHttpClientFactory httpClientFactory, IConfiguration configuration, TcpGatewayService tcpGatewayService)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _tcpGatewayService = tcpGatewayService;
        }

        // Ch? d�ng StartedAsync � ??m b?o app ?� ho�n to�n s?n s�ng tr??c khi load routes
        public async Task StartedAsync(CancellationToken cancellationToken)
        {
            string urlRegistry = _configuration["services:serviceregistry:https:0"] ?? "";
            if (string.IsNullOrEmpty(urlRegistry))
            {
                Debug.LogError("[Gateway] Registry URL is not configured.");
            }
            else
            {
                await LoadExistingRoutesAsync(urlRegistry, cancellationToken);
            }

            // Start TCP listener SAU KHI ?� load routes xong
            // Kết nối tới MỌI service đã đăng ký (kể cả service push-only không có route trong /mapRouter,
            // vd LevelService/ResourceService chỉ push SMGetLevelData/SMGetResourceData).
            if (!string.IsNullOrEmpty(urlRegistry))
            {
                await ConnectToRegisteredServicesAsync(urlRegistry, cancellationToken);
            }

            Debug.Log("[Gateway] Routes loaded. Starting TCP listener...");
            _ = _tcpGatewayService.StartAsync();
        }

        /// <summary>
        /// Load c�c routes hi?n c� t? Registry v� kh?i t?o TCP connection ??n t?ng service.
        /// X? l� tr??ng h?p Gateway restart sau khi c�c services ?� running.
        /// </summary>
        private async Task LoadExistingRoutesAsync(string urlRegistry, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            try
            {
                var response = await httpClient.GetFromJsonAsync<Dictionary<uint, string>>(
                    urlRegistry + "/mapRouter", cancellationToken);

                if (response == null || response.Count == 0)
                {
                    Debug.Log("[Gateway] No existing routes found in Registry.");
                    return;
                }

                Debug.Log($"[Gateway] Loading {response.Count} existing routes from Registry...");

                // Group theo service URL ?? t?o m?t TCP connection duy nh?t cho m?i service
                var serviceGroups = response.GroupBy(kvp => kvp.Value);

                foreach (var group in serviceGroups)
                {
                    string serviceUrl = group.Key;

                    // C?p nh?t routing table
                    foreach (var hash in group.Select(kvp => kvp.Key))
                    {
                        MessageRouter.dicMessageRouter.TryAdd(hash, serviceUrl);
                    }

                    // T?o TCP connection n?u ch?a c�
                    if (!MessageRouter.dicConnectControllerInternal.ContainsKey(serviceUrl))
                    {
                        try
                        {
                            // Parse "IP:Port" tr?c ti?p, kh�ng d�ng hack https://
                            var separatorIndex = serviceUrl.LastIndexOf(':');
                            if (separatorIndex < 0 || !int.TryParse(serviceUrl[(separatorIndex + 1)..], out int port))
                            {
                                Debug.LogError($"[Gateway] Invalid serviceUrl format '{serviceUrl}'. Expected 'IP:Port'.");
                                continue;
                            }
                            string host = serviceUrl[..separatorIndex];
                            var tcpClient = new TcpClient();
                            await tcpClient.ConnectAsync(host, port, cancellationToken);
                            var controller = new StreamConnectServiceToGateway(tcpClient);
                            MessageRouter.dicConnectControllerInternal[serviceUrl] = controller;
                            Debug.Log($"[Gateway] Connected to existing service at {serviceUrl}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[Gateway] Failed to connect to {serviceUrl}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Error loading existing routes: {ex.Message}");
            }
        }

        /// <summary>
        /// Lấy danh sách MỌI service đã đăng ký từ Registry (GET /services) và kết nối tới từng service.
        /// Đảm bảo Gateway kết nối được cả service push-only (không xuất hiện trong /mapRouter vì không sở hữu route nào).
        /// </summary>
        private async Task ConnectToRegisteredServicesAsync(string urlRegistry, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            try
            {
                var services = await httpClient.GetFromJsonAsync<List<string>>(
                    urlRegistry + "/services", cancellationToken);

                if (services == null || services.Count == 0)
                {
                    Debug.Log("[Gateway] No registered services found in Registry.");
                    return;
                }

                Debug.Log($"[Gateway] Ensuring connection to {services.Count} registered service(s)...");
                foreach (var serviceUrl in services)
                {
                    await TryConnectToServiceAsync(serviceUrl, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Error loading registered services: {ex.Message}");
            }
        }

        /// <summary>
        /// Mở TCP connection tới một service (bỏ qua nếu đã kết nối). Parse "IP:Port" trực tiếp.
        /// </summary>
        private async Task TryConnectToServiceAsync(string serviceUrl, CancellationToken cancellationToken)
        {
            if (MessageRouter.dicConnectControllerInternal.ContainsKey(serviceUrl))
                return;

            try
            {
                var separatorIndex = serviceUrl.LastIndexOf(':');
                if (separatorIndex < 0 || !int.TryParse(serviceUrl[(separatorIndex + 1)..], out int port))
                {
                    Debug.LogError($"[Gateway] Invalid serviceUrl format '{serviceUrl}'. Expected 'IP:Port'.");
                    return;
                }
                string host = serviceUrl[..separatorIndex];
                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port, cancellationToken);
                var controller = new StreamConnectServiceToGateway(tcpClient);
                MessageRouter.dicConnectControllerInternal[serviceUrl] = controller;
                Debug.Log($"[Gateway] Connected to registered service at {serviceUrl}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Failed to connect to {serviceUrl}: {ex.Message}");
            }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
