using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceShare.Database;

namespace ServiceShare.MongoDB
{
    /// <summary>
    /// Extension methods for registering the shared MongoDB infrastructure via DI.
    /// </summary>
    public static class MongoDbExtensions
    {
        /// <summary>
        /// Registers <see cref="MongoDBSettings"/>, <see cref="IMongoService"/>, and <see cref="MongoService"/>
        /// as singletons. When running under Aspire, the connection string is resolved from
        /// <c>ConnectionStrings:{aspireKey}</c>; otherwise falls back to the "MongoDB" section
        /// in appsettings.json.
        /// </summary>
        /// <param name="services">The DI service collection.</param>
        /// <param name="configuration">Application configuration.</param>
        /// <param name="aspireConnectionStringKey">
        /// The Aspire-injected connection string key (e.g. "leveldb", "resourcedb", "authdb").
        /// </param>
        public static IServiceCollection AddMongoDb(
            this IServiceCollection services,
            IConfiguration configuration,
            string aspireConnectionStringKey)
        {
            // Step 1: Bind MongoDBSettings — prefer Aspire-injected string, fall back to appsettings
            services.Configure<MongoDBSettings>(settings =>
            {
                var aspireConnectionString = configuration.GetConnectionString(aspireConnectionStringKey);
                if (!string.IsNullOrEmpty(aspireConnectionString))
                {
                    settings.ConnectionString = aspireConnectionString;
                    // BUG có sẵn từ trước, phát hiện khi verify runtime: DatabaseName ưu tiên đọc
                    // appsettings.json "MongoDB:DatabaseName" (giá trị fallback dùng khi KHÔNG chạy qua
                    // Aspire) ngay cả khi đang ở nhánh Aspire — khiến LevelService/ResourceService ghi
                    // nhầm vào database "Microservices" (giá trị hardcode trong appsettings.json) thay vì
                    // đúng database Aspire đã cấp ("leveldb"/"resourcedb"). Khi đã dùng Aspire connection
                    // string, tên database LUÔN phải khớp đúng key Aspire tương ứng.
                    settings.DatabaseName = aspireConnectionStringKey;
                }
                else
                {
                    configuration.GetSection("MongoDB").Bind(settings);
                }
            });

            // Step 2: Register the shared MongoDB service
            services.AddSingleton<IMongoService, MongoService>();

            return services;
        }

        /// <summary>
        /// Registers <see cref="IDbCollection{TDocument}"/> as a singleton backed by MongoDB.
        /// Must be called after <see cref="AddMongoDb"/> so that <see cref="IMongoService"/> is available.
        /// </summary>
        /// <typeparam name="TDocument">The document type for this collection.</typeparam>
        /// <param name="services">The DI service collection.</param>
        /// <param name="collectionName">MongoDB collection name (e.g. "Level", "Resource", "Account").</param>
        public static IServiceCollection AddMongoCollection<TDocument>(
            this IServiceCollection services,
            string collectionName)
        {
            services.AddSingleton<IDbCollection<TDocument>>(sp =>
                new MongoDbCollection<TDocument>(
                    sp.GetRequiredService<IMongoService>(),
                    collectionName));
            return services;
        }
    }
}
