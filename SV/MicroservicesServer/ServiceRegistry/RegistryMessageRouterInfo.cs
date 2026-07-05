using System.Text.Json.Serialization;

namespace ServiceRegistry
{
    /// <summary>
    /// Thông tin đăng ký service với Service Registry
    /// </summary>
    public record RegistryMessageRouterInfo(
        string urlService, 
        List<uint> hashMessage,
        string serviceName = "Unknown" // Tên service để dễ tracking
    );
}
