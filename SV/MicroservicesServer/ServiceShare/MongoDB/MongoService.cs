using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace ServiceShare.MongoDB
{
    /// <summary>
    /// Shared MongoDB service implementation.
    /// Connects to the database using settings bound from configuration.
    /// </summary>
    public class MongoService : IMongoService
    {
        public IMongoDatabase Database { get; }

        public MongoService(IOptions<MongoDBSettings> options)
        {
            var client = new MongoClient(options.Value.ConnectionString);
            Database = client.GetDatabase(options.Value.DatabaseName);
        }
    }
}
