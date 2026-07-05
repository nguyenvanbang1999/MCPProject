using MessagePack;

namespace ServiceShare.EventBus
{
    /// <summary>
    /// Event ???c publish khi có service m?i ??ng ký v?i Service Registry.
    /// Gateway s? consume event này ?? k?t n?i ??n service m?i.
    /// </summary>
    [MessagePackObject]
    public class ServiceRegisteredEvent
    {
        /// <summary>
        /// URL c?a service (format: "IP:Port", ví d?: "192.168.1.100:12345")
        /// </summary>
        [Key(0)]
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Tên service (ví d?: "AuthService", "GameService")
        /// </summary>
        [Key(1)]
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Danh sách hash c?a các message types mà service x? lý
        /// </summary>
        [Key(2)]
        public List<uint> MessageHashes { get; set; } = new List<uint>();

        /// <summary>
        /// Th?i ?i?m ??ng ký
        /// </summary>
        [Key(3)]
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    }
}
