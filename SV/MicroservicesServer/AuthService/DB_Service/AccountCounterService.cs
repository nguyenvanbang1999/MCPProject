using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AccountService.DB_Service
{
    /// <summary>
    /// Counter service for generating auto-incremented user IDs in MongoDB.
    /// Uses a static singleton initialized from the shared IMongoService via Initialize().
    /// </summary>
    public class AccountCounterService
    {
        private readonly IMongoCollection<Counter> _counter;
        private static AccountCounterService? _instance;

        /// <summary>
        /// Gets the singleton instance. Throws if Initialize() has not been called.
        /// </summary>
        public static AccountCounterService Instance =>
            _instance ?? throw new InvalidOperationException("AccountCounterService has not been initialized. Call Initialize() first.");

        /// <summary>
        /// Initializes the singleton with a MongoDB database from shared IMongoService.
        /// Must be called once from Program.cs before the application starts serving requests.
        /// </summary>
        public static void Initialize(IMongoDatabase database)
        {
            _instance = new AccountCounterService(database);
        }

        private AccountCounterService(IMongoDatabase database)
        {
            _counter = database.GetCollection<Counter>("Counter");
        }

        public async Task<uint> GetNextUserIdAsync()
        {
            var filter = Builders<Counter>.Filter.Eq(c => c.CounterID, "user_id");
            var update = Builders<Counter>.Update.Inc(c => c.Value, 1U);

            var options = new FindOneAndUpdateOptions<Counter>
            {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = true
            };

            var updatedCounter = await _counter.FindOneAndUpdateAsync(filter, update, options).ConfigureAwait(false);
            return updatedCounter.Value;
        }

        [BsonIgnoreExtraElements]
        public class Counter
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonElement("counter_id")]
            public string CounterID { get; set; } = null!;

            [BsonElement("value")]
            public uint Value { get; set; } = 0;
        }
    }
}

