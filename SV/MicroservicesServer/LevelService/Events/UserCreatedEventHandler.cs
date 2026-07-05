using AccountService.Contracts;
using LevelService.Contracts;
using LevelService.DB_Service;
using ServiceRegistry.SevicesControl;
using ServiceShare.EventBus;
using ServiceShare.StoreObject;

namespace LevelService.Events
{
    /// <summary>
    /// Handles UserCreatedEvent published by AccountService.
    /// Initializes default level data for newly created users, persists it to MongoDB,
    /// and sends it to the Gateway.
    /// </summary>
    public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
    {
        private const long DEFAULT_EXP_TO_NEXT_LEVEL = 1000;

        private readonly ILevelRepository _levelRepository;
        private readonly IGatewaySender _gatewaySender;

        public UserCreatedEventHandler(ILevelRepository levelRepository, IGatewaySender gatewaySender)
        {
            _levelRepository = levelRepository;
            _gatewaySender = gatewaySender;
        }

        /// <summary>Subscribes to the user-events Kafka topic.</summary>
        public string Topic => "user-events";

        /// <summary>
        /// Creates default level data (Level 1, Exp 0) for the new user,
        /// saves it to MongoDB, and forwards it to the Gateway.
        /// </summary>
        public async Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            // Filter out non-creation events deserialized from the same topic
            if (@event.UserId == 0)
                return;

            Console.WriteLine($"[LevelService] Received UserCreatedEvent for userId: {@event.UserId}");

            LevelData _levelData = new LevelData
            {
                currentLevel = 1
            };
            StoreObjectByUserId<LevelData> storeObject = new StoreObjectByUserId<LevelData>(@event.UserId, _levelData);

            // Step 1: Persist default level data to MongoDB
            await _levelRepository.CreateAsync(storeObject);
            Console.WriteLine($"[LevelService] Saved default LevelData to MongoDB for userId: {@event.UserId}");

            // Step 2: Send level data to Gateway
            var message = new SMGetLevelData
            {
                userId = @event.UserId,
                levelData = _levelData
            };

            if (!_gatewaySender.TrySend(message))
            {
                Console.WriteLine("[LevelService] Gateway connection not established. Cannot send level data.");
                return;
            }

            Console.WriteLine($"[LevelService] Sent default SMGetLevelData to Gateway for userId: {@event.UserId}");
        }
    }
}
