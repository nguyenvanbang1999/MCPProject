using MessagePack;
using SharedContracts.Messages;

namespace ResourceService.Contracts
{
    /// <summary>
    /// Response message sent from ResourceService to the client via Gateway.
    /// Contains the player's current resource data.
    /// </summary>
    [MessagePackObject]
    public class SMGetResourceData : MessageBase
    {
        /// <summary>Current gold amount of the player.</summary>
        [Key(2)]
        public long Gold;

        /// <summary>Current gem amount of the player.</summary>
        [Key(3)]
        public long Gem;
    }
}
