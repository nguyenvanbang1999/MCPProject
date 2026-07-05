using ResourceService.Contracts;
using SharedContracts.Messages;

namespace GateWayTCP
{
    /// <summary>
    /// Handles SMGetResourceData messages received from ResourceService.
    /// Routing to the client is handled automatically by StreamConnectServiceToGateway.
    /// </summary>
    public class SMGetResourceDataReviceController : MessageReviceController<SMGetResourceData>
    {
        protected override void OnReveive(SMGetResourceData message)
        {
            Console.WriteLine($"[GatewayTCP] Received SMGetResourceData from ResourceService for userId: {message.userId} - Gold: {message.Gold}, Gem: {message.Gem}");
        }
    }
}
