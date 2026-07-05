using LevelService.Contracts;
using SharedContracts.Messages;

namespace GateWayTCP
{
    /// <summary>
    /// Handles SMGetLevelData messages received from LevelService.
    /// Routing to the client is handled automatically by StreamConnectServiceToGateway.
    /// </summary>
    public class SMGetLevelDataReviceController : MessageReviceController<SMGetLevelData>
    {
        protected override void OnReveive(SMGetLevelData message)
        {
            Console.WriteLine($"[GatewayTCP] Received SMGetLevelData from LevelService for userId: {message.userId} - Level: {message.levelData.currentLevel}");
        }
    }
}
