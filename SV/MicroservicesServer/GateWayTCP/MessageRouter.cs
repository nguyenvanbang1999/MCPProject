namespace GateWayTCP
{
    /// <summary>
    /// Routes messages between clients and internal microservices based on message type.
    /// Maintains connection mappings for both client connections and service connections.
    /// </summary>
    public class MessageRouter
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        
        /// <summary>
        /// Maps message type IDs to service URLs for routing.
        /// Key: Message type ID (hash of message class name)
        /// Value: Service URL (host:port) that handles this message type
        /// </summary>
        public static Dictionary<uint, string> dicMessageRouter = new Dictionary<uint, string>();

        /// <summary>
        /// Maps service URLs to their TCP connections.
        /// Key: Service URL (host:port)
        /// Value: Stream connection controller for communication with the internal service
        /// </summary>
        public static Dictionary<string, StreamConnectServiceToGateway> dicConnectControllerInternal = new Dictionary<string, StreamConnectServiceToGateway>();

        /// <summary>
        /// Maps user IDs to their client connections.
        /// Key: User ID or device ID
        /// Value: Stream connection controller for communication with the client
        /// </summary>
        public static Dictionary<uint, StreamConnectClientGateWay> dicConnectController = new Dictionary<uint, StreamConnectClientGateWay>();
        /// <summary>
        /// Maps guest identifiers to their client connections.
        /// Key: Guest identifier (string)
        /// Value: Stream connection controller for communication with the guest client
        /// </summary>
        public static Dictionary<string, StreamConnectClientGateWay> dicGuestConnectController = new Dictionary<string, StreamConnectClientGateWay>();

        /// <summary>
        /// Initializes a new instance of the MessageRouter class.
        /// </summary>
        /// <param name="httpFactory">HTTP client factory for making service registry requests</param>
        /// <param name="config">Application configuration</param>
        public MessageRouter(IHttpClientFactory httpFactory, IConfiguration config)
        {
            _httpFactory = httpFactory;
            _config = config;
        }
    }
}
