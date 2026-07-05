using MessagePack;

namespace SharedContracts.Messages
{
    /// <summary>
    /// Base class for all messages in the microservices system.
    /// All game messages should inherit from this class.
    /// Messages are automatically serialized using MessagePack for efficient network transmission.
    /// </summary>
    [MessagePackObject]
    public class MessageBase
    {
        /// <summary>
        /// Gets or sets the user ID that this message is associated with.
        /// Used for routing responses back to the correct client.
        /// </summary>
        [Key(0)]
        public uint userId;
        
        /// <summary>
        /// Gets or sets the message ID for this specific message instance.
        /// Can be used for message tracking and correlation.
        /// </summary>
        [Key(1)]
        public ushort messageId;

        /// <summary>
        /// Gets the unique type identifier for this message class.
        /// Calculated using MurmurHash3 of the class name.
        /// Used by the gateway to route messages to the correct service.
        /// </summary>
        [IgnoreMember]
        public uint MessageTypeId
        {
            get
            {
                return MessageUtil.GetMessageTypeId(GetType());
            }
        }


    }

}
