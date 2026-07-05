using MessagePack;
using SharedContracts.Messages;

namespace AccountService.Contracts
{
    [MessagePackObject]
    public class CMLogin: MessageBase
    {
        [Key(2)]
        public string deviceId;
    }
}
