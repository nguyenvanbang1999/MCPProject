using Microsoft.AspNetCore.Mvc;
using SharedContracts.ConnectController;
using SharedContracts.LogUltil;
using SharedContracts.Messages;
using System;
using System.Net.Sockets;

namespace GateWayTCP
{
    /// <summary>
    /// Handles TCP connections from internal microservices to the gateway.
    /// Receives responses from services and routes them back to the appropriate clients.
    /// </summary>
    public class StreamConnectServiceToGateway : StreamConnectControllerBinary
    {
        /// <summary>
        /// Initializes a new service-to-gateway connection.
        /// </summary>
        /// <param name="client">TCP client connection from the internal service</param>
        /// <param name="sizeBuffer">Buffer size for reading data (default: 1024 bytes)</param>
        public StreamConnectServiceToGateway(TcpClient client, int sizeBuffer = 1024) : base(client, sizeBuffer)
        {
        }


        /// <summary>
        /// Processes messages received from internal services.
        /// Routes the response back to the appropriate client based on user ID.
        /// </summary>
        /// <param name="data">Raw message bytes received from service</param>
        /// <param name="ackId">Acknowledgment ID if reliable delivery is required</param>
        protected override void OnReadMessage(byte[] data, ushort ackId)
        {
            // Xử lý dữ liệu đọc được từ dịch vụ nội bộ qua gateway
            // Ví dụ: Chuyển tiếp dữ liệu đến client tương ứng
            try
            {
                Debug.Log($"[Gateway] Received data from internal service: OnReadMessage");
                if (data == null)
                {
                    return;
                }
                var message = MessageUtil.DeserializeMessage(data);
                MessageUtil.OnReviceMessage(message);

                StreamConnectClientGateWay? clientConnect = null;

                // Step 1: Ưu tiên tìm theo userId (đã login)
                if (message.userId != 0)
                {
                    MessageRouter.dicConnectController.TryGetValue(message.userId, out clientConnect);
                }

                // Step 2: Nếu chưa có userId (ví dụ SMLogin response), tìm theo deviceId trong dicGuestConnectController
                if (clientConnect == null && message is AccountService.Contracts.SMLogin smLogin && !string.IsNullOrEmpty(smLogin.deviceId))
                {
                    MessageRouter.dicGuestConnectController.TryGetValue(smLogin.deviceId, out clientConnect);

                    // Step 3: Nếu SMLogin có userId hợp lệ, chuyển client từ guest sang authenticated
                    if (clientConnect != null && smLogin.userId != 0)
                    {
                        MessageRouter.dicGuestConnectController.Remove(smLogin.deviceId);
                        MessageRouter.dicConnectController[smLogin.userId] = clientConnect;
                        Debug.Log($"[Gateway] Promoted guest '{smLogin.deviceId}' to authenticated userId {smLogin.userId}");
                    }
                }

                if (clientConnect != null)
                {
                    clientConnect.SendMessage(data, ackId != 0);
                    Debug.Log($"[Gateway] Sent response to userId {message.userId}");
                }
                else
                {
                    Debug.LogError($"[Gateway] No client connection found for userId {message.userId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Gateway] Error in OnReadMessage from internal service: {ex.Message}:{ex.StackTrace}");
            }
        }
    }
}
