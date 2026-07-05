using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace TestClient.Mcp
{
    /// <summary>
    /// Custom MCP tools that let an MCP client (Claude) drive the Test Client smoke test:
    /// Login -> Start level -> End level (Win/Lose), plus a status probe.
    ///
    /// These are Editor-only tools. They run on the Unity main thread inside the MCP bridge
    /// and reach into the running game via the <c>GameManager</c> singleton, so the Editor
    /// must be in Play Mode for the game actions to work (game_login enters Play Mode for you).
    ///
    /// Each tool passes an explicit command name to [McpForUnityTool] on purpose: tool
    /// discovery strips a trailing "Tool" from class names while command routing does not,
    /// so relying on auto-naming would desync the two. Explicit names keep them aligned.
    /// </summary>
    internal static class TestClientToolHelper
    {
        /// <summary>Locate the live GameManager (only exists in Play Mode).</summary>
        internal static GameManager FindGameManager()
        {
            if (GameManager.Instance != null)
                return GameManager.Instance;
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<GameManager>();
#else
            return UnityEngine.Object.FindObjectOfType<GameManager>();
#endif
        }

        internal static string ActiveSceneName()
        {
            try { return SceneManager.GetActiveScene().name; }
            catch { return null; }
        }

        /// <summary>Snapshot of the client state used by every tool's response.</summary>
        internal static object Snapshot()
        {
            var gm = FindGameManager();
            return new
            {
                is_playing = EditorApplication.isPlaying,
                is_busy = EditorApplication.isCompiling || EditorApplication.isUpdating,
                active_scene = ActiveSceneName(),
                has_game_manager = gm != null,
                user_id = gm != null ? gm.UserId : null,
                logged_in = gm != null && !string.IsNullOrEmpty(gm.UserId),
                last_level_result = gm != null ? gm.LastLevelResult.ToString() : "Unknown",
            };
        }

        /// <summary>Guard: game actions require Play Mode. Returns an error to return, or null if OK.</summary>
        internal static ErrorResponse RequirePlaying()
        {
            if (!EditorApplication.isPlaying)
                return new ErrorResponse(
                    "Not in Play Mode. Call game_login first (it enters Play Mode), wait for Unity to finish, then retry.");
            return null;
        }
    }

    /// <summary>
    /// Login: boots the client and performs the TCP connect + CMLogin handshake.
    /// If not in Play Mode, this enters Play Mode (which loads the Intro scene and
    /// bootstraps GameManager) and returns immediately — call it again once Unity is ready.
    /// </summary>
    [McpForUnityTool("game_login",
        Description = "Test Client: log in. If the Editor is not in Play Mode, enters Play Mode (loads the Intro scene) and returns; call again once Unity is ready. When already playing, calls GameManager.Connect() to open the TCP connection and send the CMLogin message. On the server's SMLogin reply the client loads the Home scene. Poll game_status until logged_in is true / active_scene is 'Home'.")]
    public static class GameLoginTool
    {
        public static object HandleCommand(JObject @params)
        {
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return new ErrorResponse("Unity is compiling/importing. Wait until idle, then call game_login again.");

                EditorApplication.isPlaying = true;
                return new SuccessResponse(
                    "Entering Play Mode to boot the Test Client (Intro scene). Wait ~2-3s for Unity to finish, then call game_login again to connect and log in.",
                    new { state = "entering_play_mode", is_playing = false });
            }

            var gm = TestClientToolHelper.FindGameManager();
            if (gm == null)
                return new ErrorResponse(
                    "In Play Mode but GameManager not found yet (Intro scene may still be loading). Wait a moment and retry.");

            if (!string.IsNullOrEmpty(gm.UserId))
                return new SuccessResponse($"Already logged in (userId={gm.UserId}).", TestClientToolHelper.Snapshot());

            try
            {
                gm.Connect();
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start login: {e.Message}");
            }

            return new SuccessResponse(
                "Login initiated: connecting TCP and sending CMLogin. On the server's SMLogin reply the client loads the Home scene. Poll game_status until logged_in is true. If it stalls, check read_console for connection errors and verify DemoTCP host/port.",
                new { state = "logging_in", snapshot = TestClientToolHelper.Snapshot() });
        }
    }

    /// <summary>
    /// Start level: enters the Level scene (GameManager.PlayLevel), resetting LastLevelResult.
    /// </summary>
    [McpForUnityTool("game_start_level",
        Description = "Test Client: start a level. Calls GameManager.PlayLevel(), which resets LastLevelResult to None and loads the 'Level' scene. Requires Play Mode. Finish the level with game_end_level. Poll game_status until active_scene is 'Level'.")]
    public static class GameStartLevelTool
    {
        public static object HandleCommand(JObject @params)
        {
            var guard = TestClientToolHelper.RequirePlaying();
            if (guard != null) return guard;

            var gm = TestClientToolHelper.FindGameManager();
            if (gm == null)
                return new ErrorResponse("GameManager not found. Log in first with game_login.");

            try
            {
                gm.PlayLevel();
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start level: {e.Message}");
            }

            string note = string.IsNullOrEmpty(gm.UserId)
                ? " Note: no user is logged in yet; starting a level anyway."
                : "";
            return new SuccessResponse(
                "Started level: loading the 'Level' scene (LastLevelResult reset to None)." + note +
                " Finish with game_end_level (result: win|lose). Poll game_status to confirm active_scene is 'Level'.",
                new { state = "level_starting", snapshot = TestClientToolHelper.Snapshot() });
        }
    }

    /// <summary>
    /// End level: records Win/Lose (GameManager.EndLevel) and returns to the Home scene.
    /// </summary>
    [McpForUnityTool("game_end_level",
        Description = "Test Client: end the current level with a result. Calls GameManager.EndLevel(Win|Lose), which records LastLevelResult and loads the Home scene. Requires Play Mode. Pass result='win' or result='lose'.")]
    public static class GameEndLevelTool
    {
        // Nested Parameters class describes the tool's input schema for dynamic registration.
        public class Parameters
        {
            [ToolParameter("Match result to record: 'win' or 'lose'.", Required = true)]
            public string result { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var guard = TestClientToolHelper.RequirePlaying();
            if (guard != null) return guard;

            var gm = TestClientToolHelper.FindGameManager();
            if (gm == null)
                return new ErrorResponse("GameManager not found. Log in and start a level first.");

            var p = new ToolParams(@params ?? new JObject());
            string raw = (p.Get("result") ?? "").Trim().ToLowerInvariant();

            GameManager.LevelResult result;
            switch (raw)
            {
                case "win":
                case "won":
                case "victory":
                    result = GameManager.LevelResult.Win;
                    break;
                case "lose":
                case "lost":
                case "loss":
                case "defeat":
                    result = GameManager.LevelResult.Lose;
                    break;
                default:
                    return new ErrorResponse("Parameter 'result' is required and must be 'win' or 'lose'.");
            }

            try
            {
                gm.EndLevel(result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to end level: {e.Message}");
            }

            return new SuccessResponse(
                $"Ended level with result '{result}'. The client returns to the Home scene. Poll game_status to confirm active_scene is 'Home' and last_level_result is '{result}'.",
                new { state = "level_ended", result = result.ToString(), snapshot = TestClientToolHelper.Snapshot() });
        }
    }

    /// <summary>
    /// Status probe: read-only snapshot of the running client (Play Mode, scene, login, result).
    /// Use it to verify each step when automating the smoke test.
    /// </summary>
    [McpForUnityTool("game_status",
        Description = "Test Client: read-only status of the running client — is_playing, active_scene, has_game_manager, logged_in, user_id, last_level_result. Use it to verify each step (login -> start_level -> end_level) when driving the client automatically.")]
    public static class GameStatusTool
    {
        public static object HandleCommand(JObject @params)
        {
            string message = EditorApplication.isPlaying
                ? "Test Client status."
                : "Editor is not in Play Mode; the Test Client only runs during Play Mode. Call game_login to boot it.";
            return new SuccessResponse(message, TestClientToolHelper.Snapshot());
        }
    }
}
