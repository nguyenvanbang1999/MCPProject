using AccountService.Contracts;
using SharedContracts.Messages;

namespace GateWayTCP
{
    public class SMLoginReviceController : MessageReviceController<SMLogin>
    {
        protected override void OnReveive(SMLogin message)
        {
            Console.WriteLine("GateWayTCP: Nhận phản hồi đăng nhập từ AccountService: " + message.userId);
            if (message == null)
            {
                return;
            }
            // Xử lý logic khi nhận được phản hồi đăng nhập từ AuthService
            bool hasConnect = MessageRouter.dicGuestConnectController.TryGetValue(message.deviceId, out StreamConnectClientGateWay? connect);
            if (hasConnect)
            {
                MessageRouter.dicGuestConnectController.Remove(message.deviceId);
            }
            if (message.userId != 0 && connect != null)
            {
                MessageRouter.dicConnectController[message.userId] = connect;
            }
            else
            {
                connect?.Close();
            }
        }
    }
}
