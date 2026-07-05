using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace ServiceShare.EventBus;

/// <summary>
/// Configuration for a single Kafka consumer subscription
/// </summary>
public class ConsumerSubscription
{
    /// <summary>
    /// Kafka topic to subscribe to
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Handler type to process the events
    /// </summary>
    public Type HandlerType { get; set; } = typeof(IEventHandler<>);
}

/// <summary>
/// Background service that consumes events from Kafka topics
/// </summary>
public class KafkaConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly KafkaSettings _settings;
    private readonly List<ConsumerSubscription> _subscriptions;
    private IConsumer<string, string>? _consumer;
    private List<IEventHandler> _handlerInstances = [];
    // Giữ các scope sống suốt vòng đời consumer để handler có thể dùng DI services
    private readonly List<IServiceScope> _handlerScopes = [];

    public KafkaConsumerService(
        IServiceProvider serviceProvider,
        IOptions<KafkaSettings> settings,
        ILogger<KafkaConsumerService> logger,
        IEnumerable<ConsumerSubscription> subscriptions)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
        _subscriptions = subscriptions.ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Step 1: Resolve handler instances — ưu tiên từ DI subscriptions, fallback scan assembly
        _handlerInstances = [];

        if (_subscriptions.Count > 0)
        {
            // Dùng subscription đã đăng ký qua AddKafkaConsumer().Subscribe<>()
            // Giữ scope sống (không using) để handler có thể dùng injected services
            foreach (var subscription in _subscriptions)
            {
                try
                {
                    var scope = _serviceProvider.CreateScope();
                    var handler = (IEventHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, subscription.HandlerType);
                    _handlerInstances.Add(handler);
                    _handlerScopes.Add(scope);
                    _logger.LogInformation("Registered event handler from DI: {HandlerType}", subscription.HandlerType.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create handler {HandlerType} from DI", subscription.HandlerType.Name);
                }
            }
        }
        else
        {
            // Fallback: scan tất cả assembly (dùng khi không có subscription nào được đăng ký)
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var handlerTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract &&
                               t.GetInterfaces().Any(i => i.IsGenericType &&
                               i.GetGenericTypeDefinition() == typeof(IEventHandler<>)));

                    foreach (var handlerType in handlerTypes)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var handler = (IEventHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, handlerType);
                        _handlerInstances.Add(handler);
                        _logger.LogInformation("Registered event handler (assembly scan): {HandlerType}", handlerType.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan assembly {AssemblyName}", assembly.FullName);
                }
            }
        }

        _logger.LogInformation("Total event handlers registered: {Count}", _handlerInstances.Count);

        if (_handlerInstances.Count == 0)
        {
            _logger.LogWarning("No IEventHandler implementations found. Consumer will not start.");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            EnableAutoCommit = _settings.EnableAutoCommit,
            AutoCommitIntervalMs = _settings.AutoCommitIntervalMs,
            SessionTimeoutMs = _settings.SessionTimeoutMs,
            MaxPollIntervalMs = _settings.MaxPollIntervalMs,
            AutoOffsetReset = ParseAutoOffsetReset(_settings.AutoOffsetReset),
            EnablePartitionEof = false
        };

        _consumer = new ConsumerBuilder<string, string>(config)
        .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: {ErrorCode} - {ErrorReason}", error.Code, error.Reason);
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
        
        var topics = _handlerInstances.Select(s => s.Topic).Distinct().ToList();
        _consumer.Subscribe(topics);

        _logger.LogInformation(
          "Kafka consumer started: GroupId={GroupId}, Topics={Topics}",
              _settings.ConsumerGroupId, string.Join(", ", topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message == null)
                        continue;

                    await ProcessMessageAsync(consumeResult, stoppingToken);

                    if (!_settings.EnableAutoCommit)
                    {
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    // Topic not yet created (auto-created on first publish) — retry silently
                    if (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                    {
                        _logger.LogWarning("Topic not yet available, retrying in 5s: {Reason}", ex.Error.Reason);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex, "Error consuming message: {Error}", ex.Error.Reason);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Consumer operation cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in consumer loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _consumer.Close();
            _consumer.Dispose();
            _logger.LogInformation("Kafka consumer stopped");
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        var topic = consumeResult.Topic;
        var matchingHandlers = _handlerInstances.Where(h => h.Topic == topic).ToList();

        if (matchingHandlers.Count == 0)
        {
            _logger.LogWarning("No handler found for topic: {Topic}", topic);
            return;
        }

        foreach (var handler in matchingHandlers)
        {
            try
            {
                // Step 1: Determine TEvent type from IEventHandler<TEvent>
                var eventHandlerInterface = handler.GetType().GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));
                var eventType = eventHandlerInterface.GetGenericArguments()[0];

                // Step 2: Parse JSON payload as JToken, then deserialize to target event type
                var jToken = JToken.Parse(consumeResult.Message.Value);
                var @event = jToken.ToObject(eventType);

                // Step 3: Invoke HandleAsync via reflection
                var handleMethod = eventHandlerInterface.GetMethod(nameof(IEventHandler<object>.HandleAsync));
                await (Task)handleMethod!.Invoke(handler, [@event, cancellationToken])!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message on topic: {Topic}, Key: {Key}",
                    topic, consumeResult.Message.Key);
            }
        }
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value)
    {
        return value.ToLower() switch
        {
            "earliest" => AutoOffsetReset.Earliest,
            "latest" => AutoOffsetReset.Latest,
            "none" => AutoOffsetReset.Error,
            _ => AutoOffsetReset.Earliest
        };
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        // Giải phóng tất cả handler scopes
        foreach (var scope in _handlerScopes)
        {
            scope.Dispose();
        }
        _handlerScopes.Clear();
        base.Dispose();
    }
}
