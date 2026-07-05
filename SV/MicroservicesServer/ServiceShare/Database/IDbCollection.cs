using System.Linq.Expressions;

namespace ServiceShare.Database
{
    /// <summary>
    /// Database-agnostic interface for basic CRUD operations on a named collection.
    /// MongoDB is the default implementation; swap by registering a different class in DI.
    /// </summary>
    /// <typeparam name="TDocument">The document / entity type stored in the collection.</typeparam>
    public interface IDbCollection<TDocument>
    {
        /// <summary>Inserts a new document into the collection.</summary>
        Task InsertAsync(TDocument document);

        /// <summary>Returns the first document matching the filter, or null if none found.</summary>
        Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filter);

        /// <summary>Returns all documents matching the filter.</summary>
        Task<List<TDocument>> FindManyAsync(Expression<Func<TDocument, bool>> filter);

        /// <summary>
        /// Replaces the first document matching the filter with the given document.
        /// Returns true if at least one document was replaced.
        /// </summary>
        Task<bool> ReplaceAsync(Expression<Func<TDocument, bool>> filter, TDocument document);

        /// <summary>
        /// Deletes the first document matching the filter.
        /// Returns true if at least one document was deleted.
        /// </summary>
        Task<bool> DeleteAsync(Expression<Func<TDocument, bool>> filter);
    }
}
