using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ServiceShare.Database;

namespace ResourceService.DB_Service
{
    /// <summary>
    /// MongoDB-backed implementation of <see cref="IResourceRepository"/>.
    /// Depends only on <see cref="IDbCollection{ResourceDocument}"/>; swap to a different
    /// database by registering a different IDbCollection implementation in DI.
    /// </summary>
    public class ResourceRepository : IResourceRepository
    {
        private readonly IDbCollection<ResourceDocument> _resources;

        public ResourceRepository(IDbCollection<ResourceDocument> resources)
        {
            _resources = resources;
        }

        /// <inheritdoc/>
        public async Task CreateAsync(uint userId, long gold, long gem)
        {
            var document = new ResourceDocument
            {
                UserId = userId,
                Gold = gold,
                Gem = gem
            };
            await _resources.InsertAsync(document);
        }

        /// <inheritdoc/>
        public async Task<ResourceDocument?> GetByUserIdAsync(uint userId) =>
            await _resources.FindOneAsync(d => d.UserId == userId);
    }

    /// <summary>
    /// Document model representing a user's resource data in the database.
    /// Bson attributes are MongoDB serialization hints and do not affect business logic.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class ResourceDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("user_id")]
        public uint UserId { get; set; }

        [BsonElement("gold")]
        public long Gold { get; set; }

        [BsonElement("gem")]
        public long Gem { get; set; }
    }
}
