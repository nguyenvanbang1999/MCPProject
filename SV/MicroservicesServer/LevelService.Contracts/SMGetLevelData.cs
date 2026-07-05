using MessagePack;
using SharedContracts.Messages;

namespace LevelService.Contracts
{
    /// <summary>
    /// Response message sent from LevelService to the client via Gateway.
    /// Contains the player's current level data.
    /// </summary>
    [MessagePackObject]
    public class SMGetLevelData : MessageBase
    {
        /// <summary>Current level of the player.</summary>
        [Key(2)]
        public LevelData levelData;

    }
}
