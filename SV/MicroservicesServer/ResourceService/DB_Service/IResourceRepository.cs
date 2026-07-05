namespace ResourceService.DB_Service
{
    /// <summary>
    /// Database-agnostic interface for resource data persistence.
    /// Business logic depends only on this interface, not on any specific database technology.
    /// </summary>
    public interface IResourceRepository
    {
        /// <summary>Persists default resource data for a newly created user.</summary>
        Task CreateAsync(uint userId, long gold, long gem);

        /// <summary>Returns the resource document for the given user, or null if not found.</summary>
        Task<ResourceDocument?> GetByUserIdAsync(uint userId);
    }
}
