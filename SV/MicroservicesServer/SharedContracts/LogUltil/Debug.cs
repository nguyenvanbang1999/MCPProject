using System;

namespace SharedContracts.LogUltil
{
    /// <summary>
    /// Lightweight static logging facade for shared contracts.
    /// If no custom logger is injected or DI hasn't created DefaultLog yet,
    /// it falls back to a simple console logger to avoid NullReferenceException.
    /// </summary>
    public class Debug
    {
        private static IMyLogger _logger; // backing field

        private static readonly IMyLogger _fallbackLogger = new ConsoleFallbackLogger();

        private static IMyLogger Logger
        {
            get
            {
                // Step1: If already set, return
                if (_logger != null)
                {
                    return _logger;
                }

                // Step2: Try to use DefaultLog if DI created it
                if (DefaultLog.Instance != null)
                {
                    _logger = DefaultLog.Instance;
                    return _logger;
                }

                // Step3: Fallback to console logger (safe, no DI required)
                _logger = _fallbackLogger;
                return _logger;
            }
        }

        /// <summary>
        /// Inject a custom logger implementation.
        /// </summary>
        public static void InjectLogger(IMyLogger customLogger)
        {
            _logger = customLogger ?? _fallbackLogger;
        }

        /// <summary>
        /// Write information log.
        /// </summary>
        public static void Log(object message)
        {
            Logger.Log(message);
        }

        /// <summary>
        /// Write error log.
        /// </summary>
        public static void LogError(object message)
        {
            Logger.LogError(message);
        }

        /// <summary>
        /// Write warning log.
        /// </summary>
        public static void LogWarning(object message)
        {
            Logger.LogWarning(message);
        }

        /// <summary>
        /// Simple console-based logger used as a safe fallback when DI logger is not available yet.
        /// </summary>
        private sealed class ConsoleFallbackLogger : IMyLogger
        {
            public void Log(object msg)
            {
                Console.WriteLine($"[INFO] {msg}");
            }

            public void LogWarning(object msg)
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARN] {msg}");
                Console.ForegroundColor = prev;
            }

            public void LogError(object msg)
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {msg}");
                Console.ForegroundColor = prev;
            }
        }
    }
}
