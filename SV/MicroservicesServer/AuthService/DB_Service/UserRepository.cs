using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ServiceShare.Database;

namespace AccountService.DB_Service
{
    /// <summary>
    /// Repository for storing and retrieving user account data.
    /// Uses a static singleton initialized via <see cref="Initialize"/> from Program.cs,
    /// because <c>CMLoginReviceCtrl</c> is instantiated through <c>Activator.CreateInstance</c>
    /// (no DI support). The class itself has no dependency on any specific database technology.
    /// </summary>
    public class UserRepository
    {
        private readonly IDbCollection<User> _users;
        private static UserRepository? _instance;

        /// <summary>
        /// Gets the singleton instance. Throws if <see cref="Initialize"/> has not been called.
        /// </summary>
        public static UserRepository Instance =>
            _instance ?? throw new InvalidOperationException("UserRepository has not been initialized. Call Initialize() first.");

        /// <summary>
        /// Initializes the singleton with a database-agnostic collection.
        /// Must be called once from Program.cs before the application starts serving requests.
        /// </summary>
        public static void Initialize(IDbCollection<User> users)
        {
            _instance = new UserRepository(users);
        }

        private UserRepository(IDbCollection<User> users)
        {
            _users = users;
        }

        public async Task<User?> GetByDeviceIdAsync(string deviceId) =>
            await _users.FindOneAsync(u => u.DeviceId == deviceId);

        public async Task CreateAsync(User user) =>
            await _users.InsertAsync(user);
    }

    [BsonIgnoreExtraElements]
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("device_id")]
        public string DeviceId { get; set; } = null!;

        [BsonElement("user_id")]
        public uint UserId { get; set; } = 0;
    }
}

