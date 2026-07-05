using SharedContracts.LogUltil;
using SharedContracts.Messages;
using System.Net.Sockets;

namespace SharedContracts.ConnectController
{
    public class StreamConnectControllerMessage : StreamConnectController<MessageBase>
    {
        public StreamConnectControllerMessage(TcpClient client, int sizeBuffer = 1024) : base(client, sizeBuffer)
        {
        }
        protected override MessageBase DeserializeMessage(byte[] data)
        {
            return MessageUtil.DeserializeMessage(data);
        }


        protected override void OnReadMessage(MessageBase data, ushort ackId)
        {
            if (data == null)
            {
                return;
            }
            MessageUtil.OnReviceMessage(data);
            
        }
        protected override byte[] SerializeMessage(MessageBase message)
        {
            byte[] bytes = MessageUtil.SerializeMessage(message);
            Debug.Log("Check bytes: " + bytes.Length);
            return bytes;
        }
    }
}
