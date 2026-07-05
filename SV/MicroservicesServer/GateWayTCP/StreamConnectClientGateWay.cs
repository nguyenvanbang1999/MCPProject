using AccountService.Contracts;
using SharedContracts.ConnectController;
using SharedContracts.LogUltil;
using SharedContracts.Messages;
using System.Net.Sockets;
using System.Text;

namespace GateWayTCP
{
    /// <summary>
    /// Handles TCP connections from game clients to the gateway.
    /// Receives messages from clients and routes them to appropriate internal services.
    /// </summary>
    public class StreamConnectClientGateWay : StreamConnectControllerBinary
    {
        public StreamConnectClientGateWay(TcpClient client, int sizeBuffer = 1024) : base(client, sizeBuffer)
        {
            //needLogPing = true;
            Disconnected += RemoveConnectClientGateWay;
        }

        /// <summary>
        /// Processes messages received from the client.
        /// Routes the message to the appropriate internal service based on message type.
        /// </summary>
        /// <param name="data">Raw message bytes received from client</param>
        /// <param name="ackId">Acknowledgment ID if reliable delivery is required</param>
        protected override void OnReadMessage(byte[] data,ushort ackId)
        {
            // Xử lý dữ liệu đọc được từ client qua gateway
            // Ví dụ: Chuyển tiếp dữ liệu đến dịch vụ tương ứng
            if (data == null)
            {
                return;
            }
            // chuyển sang dịch vụ nội bộ
            Debug.Log("Nhận được Message từ client");
            MessageBase message = MessageUtil.DeserializeMessage(data);

            // Lưu connection vào dicGuestConnectController trước khi forward CMLogin,
            // để StreamConnectServiceToGateway có thể tìm lại client khi nhận SMLogin response.
            if (message is AccountService.Contracts.CMLogin cmLogin && !string.IsNullOrEmpty(cmLogin.deviceId))
            {
                MessageRouter.dicGuestConnectController[cmLogin.deviceId] = this;
                Debug.Log($"[Gateway] Registered guest connection for deviceId: {cmLogin.deviceId}");
            }

            if (!MessageRouter.dicMessageRouter.TryGetValue(message.MessageTypeId, out string? urlService))
            {
                Debug.LogError($"[Gateway] Không tìm thấy route cho MessageTypeId: {message.MessageTypeId}. Service chưa đăng ký hoặc chưa kết nối.");
                return;
            }

            if (!MessageRouter.dicConnectControllerInternal.TryGetValue(urlService, out StreamConnectServiceToGateway? controller))
            {
                Debug.LogError($"[Gateway] Không tìm thấy kết nối nội bộ tới service: {urlService}.");
                return;
            }

            controller.SendMessage(data, ackId != 0);
        }
        void RemoveConnectClientGateWay()
        {
            try
            {
                foreach (var item in MessageRouter.dicConnectController)
                {
                    if (item.Value == this)
                    {
                        MessageRouter.dicConnectController.Remove(item.Key);
                        Debug.Log($"[Gateway] Đã xóa kết nối Client Gateway với id: {item.Key}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Lỗi khi xóa kết nối Client Gateway: {ex.Message}");
            }
            Close();
        }
    }
}
