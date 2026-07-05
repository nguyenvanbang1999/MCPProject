using SharedContracts.Messages;

namespace ServiceRegistry.SevicesControl
{
    /// <summary>
    /// Gửi message tới Gateway. Trừu tượng hóa việc truy cập trực tiếp field tĩnh
    /// <see cref="ServiceExtentions.connectToGateway"/> — giúp: (1) kiểm tra null tập trung một chỗ
    /// thay vì lặp lại ở mọi call site, (2) mock được khi viết unit test cho các handler,
    /// (3) tránh race đọc field tĩnh 2 lần (check rồi dùng) bằng cách chụp một snapshot duy nhất.
    /// </summary>
    public interface IGatewaySender
    {
        /// <summary>
        /// Gửi message tới Gateway nếu đã có kết nối.
        /// </summary>
        /// <returns>true nếu đã gửi (có kết nối), false nếu chưa kết nối tới Gateway.</returns>
        bool TrySend(MessageBase message, bool needAck = false);
    }

    /// <inheritdoc cref="IGatewaySender"/>
    public class GatewaySender : IGatewaySender
    {
        public bool TrySend(MessageBase message, bool needAck = false)
        {
            // Chụp snapshot một lần — tránh race giữa lúc kiểm tra null và lúc dùng
            // (field tĩnh có thể bị OnGatewayDisconnected gán null ở luồng khác).
            var connection = ServiceExtentions.connectToGateway;
            if (connection == null)
            {
                return false;
            }

            connection.SendMessage(message, needAck);
            return true;
        }
    }
}
