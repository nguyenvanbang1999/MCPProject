using MongoDB.Driver;

namespace ServiceShare.MongoDB
{
    /// <summary>
    /// Provides access to a MongoDB database instance.
    /// Each service registers its own implementation pointing to its own database.
    /// </summary>
    public interface IMongoService
    {
        IMongoDatabase Database { get; }
    }
}
