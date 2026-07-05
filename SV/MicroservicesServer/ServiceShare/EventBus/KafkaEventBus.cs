using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace ServiceShare.EventBus;

/// <summary>
/// Kafka implementation of IEventBus for publishing events
/// </summary>
public class KafkaEventBus : IEventBus, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly KafkaSettings _settings;
    private bool _disposed;

    public KafkaEventBus(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventBus> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            EnableIdempotence = _settings.EnableIdempotence,
            Acks = ParseAcks(_settings.Acks),
            RetryBackoffMs = _settings.RetryBackoffMs,
            MessageTimeoutMs = 30000,
            RequestTimeoutMs = 30000
        };

        // MaxInFlight phải <= 5 khi bật idempotence (ràng buộc của Kafka protocol)
        if (_settings.EnableIdempotence)
        {
            config.MaxInFlight = Math.Min(_settings.MaxInFlight, 5);
        }

        _producer = new ProducerBuilder<string, string>(config)
                .SetErrorHandler((_, error) =>
        {
            _logger.LogError("Kafka producer error: {ErrorCode} - {ErrorReason}", error.Code, error.Reason);
        })
          .SetLogHandler((_, logMessage) =>
                {
                    var logLevel = logMessage.Level switch
                    {
                        SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical or SyslogLevel.Error => LogLevel.Error,
                        SyslogLevel.Warning => LogLevel.Warning,
                        SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
                        _ => LogLevel.Debug
                    };
                    _logger.Log(logLevel, "Kafka: {Message}", logMessage.Message);
                })
          .Build();

        _logger.LogInformation("KafkaEventBus initialized with servers: {Servers}", _settings.BootstrapServers);
    }



    /// <summary>
    /// Publish an event with a specific key to the specified topic
    /// </summary>
    public async Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            // Serialize event to JSON string using Newtonsoft.Json
            var json = JsonConvert.SerializeObject(@event);

            var message = new Message<string, string>
            {
                Key = key,
                Value = json
            };

            var deliveryResult = await _producer.ProduceAsync(topic, message, cancellationToken);

        }
        catch (ProduceException<string, string> ex)
        {
            // Trước đây bị comment out (tham chiếu @event.EventId không biên dịch được vì TEvent
            // là generic không ràng buộc) nên exception bị nuốt hoàn toàn không log.
            _logger.LogError(ex, "Failed to publish event: Topic={Topic}, Key={Key}, Error={Error}",
                topic, key, ex.Error.Reason);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing event: Topic={Topic}, Key={Key}", topic, key);
            throw;
        }
    }

    private static Acks ParseAcks(string acks)
    {
        return acks.ToLower() switch
        {
            "0" or "none" => Acks.None,
            "1" or "leader" => Acks.Leader,
            "all" or "-1" => Acks.All,
            _ => Acks.All
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing KafkaEventBus...");
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}


