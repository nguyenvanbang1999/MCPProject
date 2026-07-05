namespace LevelService.Contracts
{
    /// <summary>
    /// Forces the loading of the LevelService.Contracts assembly.
    /// Call this in any service that needs to deserialize LevelService messages.
    /// </summary>
    public static class LevelContract
    {
        /// <summary>
        /// Ensures the LevelService.Contracts assembly is loaded into the current AppDomain.
        /// This method is intentionally left blank.
        /// </summary>
        public static void Load()
        {
            // Intentionally left blank - ensures assembly is loaded.
        }
    }
}
