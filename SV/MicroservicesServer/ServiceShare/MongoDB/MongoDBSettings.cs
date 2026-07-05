namespace ServiceShare.MongoDB
{
    /// <summary>
    /// Shared MongoDB connection settings used by all services.
    /// Populated from the "MongoDB" section in appsettings.json
    /// or from the Aspire-injected connection string.
    /// </summary>
    public class MongoDBSettings
    {
        public string ConnectionString { get; set; } = null!;
        public string DatabaseName { get; set; } = null!;
    }
}
