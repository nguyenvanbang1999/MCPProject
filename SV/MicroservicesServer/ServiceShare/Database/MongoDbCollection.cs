using System.Linq.Expressions;
using MongoDB.Driver;
using ServiceShare.MongoDB;

namespace ServiceShare.Database
{
    /// <summary>
    /// MongoDB implementation of <see cref="IDbCollection{TDocument}"/>.
    /// Wraps <see cref="IMongoCollection{TDocument}"/> with db-agnostic CRUD semantics.
    /// </summary>
    /// <typeparam name="TDocument">The document type stored in the MongoDB collection.</typeparam>
    public class MongoDbCollection<TDocument> : IDbCollection<TDocument>
    {
        private readonly IMongoCollection<TDocument> _collection;

        /// <param name="mongoService">Shared MongoDB connection provided by ServiceShare.</param>
        /// <param name="collectionName">Name of the MongoDB collection (e.g. "Level", "Resource").</param>
        public MongoDbCollection(IMongoService mongoService, string collectionName)
        {
            _collection = mongoService.Database.GetCollection<TDocument>(collectionName);
        }

        /// <inheritdoc/>
        public async Task InsertAsync(TDocument document) =>
            await _collection.InsertOneAsync(document);

        /// <inheritdoc/>
        public async Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filter) =>
            await _collection.Find(filter).FirstOrDefaultAsync();

        /// <inheritdoc/>
        public async Task<List<TDocument>> FindManyAsync(Expression<Func<TDocument, bool>> filter) =>
            await _collection.Find(filter).ToListAsync();

        /// <inheritdoc/>
        public async Task<bool> ReplaceAsync(Expression<Func<TDocument, bool>> filter, TDocument document)
        {
            var result = await _collection.ReplaceOneAsync(filter, document);
            return result.ModifiedCount > 0;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(Expression<Func<TDocument, bool>> filter)
        {
            var result = await _collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }
    }
}
