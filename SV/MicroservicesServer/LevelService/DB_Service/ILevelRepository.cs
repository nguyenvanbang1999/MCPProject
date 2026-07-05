using LevelService.Contracts;
using ServiceShare.StoreObject;

namespace LevelService.DB_Service
{
    /// <summary>
    /// Database-agnostic interface for level data persistence.
    /// Business logic depends only on this interface, not on any specific database technology.
    /// </summary>
    public interface ILevelRepository
    {
        /// <summary>Persists default level data for a newly created user.</summary>
        Task CreateAsync(StoreObjectByUserId<LevelData> storeObject);

        /// <summary>Returns the level document for the given user, or null if not found.</summary>
        Task<LevelDocument?> GetByUserIdAsync(uint userId);
    }
}
