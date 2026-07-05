using MessagePack;
using SharedContracts.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace AccountService.Contracts
{
    [MessagePackObject]
    public class SMLogin:MessageBase
    {
        [Key(2)]
        public string deviceId;
    }
}
