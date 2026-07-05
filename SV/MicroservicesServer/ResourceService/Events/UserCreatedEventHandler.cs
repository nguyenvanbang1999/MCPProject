using AccountService.Contracts;
using ResourceService.Contracts;
using ResourceService.DB_Service;
using ServiceRegistry.SevicesControl;
using ServiceShare.EventBus;

namespace ResourceService.Events
{
    /// <summary>
    /// Handles UserCreatedEvent published by AccountService.
    /// Initializes default resource data for newly created users, persists it to MongoDB,
    /// and sends it to the Gateway.
    /// </summary>
    public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
    {
        private const long DEFAULT_GOLD = 100;
        private const long DEFAULT_GEM = 10;

        private readonly IResourceRepository _resourceRepository;
        private readonly IGatewaySender _gatewaySender;

        public UserCreatedEventHandler(IResourceRepository resourceRepository, IGatewaySender gatewaySender)
        {
            _resourceRepository = resourceRepository;
            _gatewaySender = gatewaySender;
        }

        /// <summary>Subscribes to the user-events Kafka topic.</summary>
        public string Topic => "user-events";

        /// <summary>
        /// Creates default resource data (Gold 100, Gem 10) for the new user,
        /// saves it to MongoDB, and forwards it to the Gateway.
        /// </summary>
        public async Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Filter out non-creation events deserialized from the same topic
            if (@event.UserId == 0)
                return;

            Console.WriteLine($"[ResourceService] Received UserCreatedEvent for userId: {@event.UserId}");

            // Step 1: Persist default resource data to MongoDB
            await _resourceRepository.CreateAsync(@event.UserId, DEFAULT_GOLD, DEFAULT_GEM);
            Console.WriteLine($"[ResourceService] Saved default ResourceData to MongoDB for userId: {@event.UserId}");

            // Step 2: Send resource data to Gateway
            var resourceData = new SMGetResourceData
            {
                userId = @event.UserId,
                Gold = DEFAULT_GOLD,
                Gem = DEFAULT_GEM
            };

            if (!_gatewaySender.TrySend(resourceData))
            {
                Console.WriteLine("[ResourceService] Gateway connection not established. Cannot send resource data.");
                return;
            }

            Console.WriteLine($"[ResourceService] Sent default SMGetResourceData to Gateway for userId: {@event.UserId}");
        }
    }
}
