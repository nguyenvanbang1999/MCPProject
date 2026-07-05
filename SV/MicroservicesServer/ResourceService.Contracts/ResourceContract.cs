namespace ResourceService.Contracts
{
    /// <summary>
    /// Forces the loading of the ResourceService.Contracts assembly.
    /// Call this in any service that needs to deserialize ResourceService messages.
    /// </summary>
    public static class ResourceContract
    {
        /// <summary>
        /// Ensures the ResourceService.Contracts assembly is loaded into the current AppDomain.
        /// This method is intentionally left blank.
        /// </summary>
        public static void Load()
        {
            // Intentionally left blank - ensures assembly is loaded.
        }
    }
}
