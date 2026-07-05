using System.Net.Sockets;

namespace SharedContracts.ConnectController
{
    public class StreamConnectControllerBinary : StreamConnectController<byte[]>
    {
        public StreamConnectControllerBinary(TcpClient client, int sizeBuffer = 1024) : base(client, sizeBuffer)
        {
        }

        protected override byte[] DeserializeMessage(byte[] data)
        {
            return data;
        }
        protected override void OnReadMessage(byte[] data, ushort ackId)
        {
            if (data == null)
            {
                return;
            }
           
        }

        protected override byte[] SerializeMessage(byte[] message)
        {
            return message;
        }

        
    }
}
