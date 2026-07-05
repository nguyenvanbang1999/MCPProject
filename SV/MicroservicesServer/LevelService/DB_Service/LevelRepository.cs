using LevelService.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ServiceShare.Database;
using ServiceShare.StoreObject;

namespace LevelService.DB_Service
{
    /// <summary>
    /// MongoDB-backed implementation of <see cref="ILevelRepository"/>.
    /// Depends only on <see cref="IDbCollection{LevelDocument}"/>; swap to a different
    /// database by registering a different IDbCollection implementation in DI.
    /// </summary>
    public class LevelRepository : ILevelRepository
    {
        private readonly IDbCollection<LevelDocument> _levels;

        public LevelRepository(IDbCollection<LevelDocument> levels)
        {
            _levels = levels;
        }

        /// <inheritdoc/>
        public async Task CreateAsync(StoreObjectByUserId<LevelData> storeObject)
        {
            var document = new LevelDocument
            {
                UserId = storeObject.userId,
                CurrentLevel = storeObject.data.currentLevel
            };
            await _levels.InsertAsync(document);
        }

        /// <inheritdoc/>
        public async Task<LevelDocument?> GetByUserIdAsync(uint userId) =>
            await _levels.FindOneAsync(d => d.UserId == userId);
    }

    /// <summary>
    /// Document model representing a user's level data in the database.
    /// Bson attributes are MongoDB serialization hints and do not affect business logic.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class LevelDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("user_id")]
        public uint UserId { get; set; }

        [BsonElement("current_level")]
        public int CurrentLevel { get; set; }
    }
}
