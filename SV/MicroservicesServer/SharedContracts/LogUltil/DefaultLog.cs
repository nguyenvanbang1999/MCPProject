using Microsoft.Extensions.Logging;

namespace SharedContracts.LogUltil
{
    public class DefaultLog: IMyLogger
    {
        public static DefaultLog Instance;
        private readonly ILogger _logger;
        public DefaultLog(ILoggerFactory factory)
        {
            _logger = factory.CreateLogger("DefaultLog");
            Instance = this;
        }
        public void Log(object msg) => _logger.LogInformation(msg.ToString());

        public void LogWarning(object msg) => _logger.LogWarning(msg.ToString());

        public void LogError(object msg) => _logger.LogError(msg.ToString());
    }
}
