namespace ServiceShare.EventBus;

/// <summary>
/// Configuration settings for Kafka
/// </summary>
public class KafkaSettings
{
    public const string SECTION_NAME = "Kafka";

    /// <summary>
    /// Bootstrap servers for Kafka connection (e.g., "localhost:9092").
    /// Ưu tiên env var ConnectionStrings__kafka (inject bởi .NET Aspire),
    /// fallback về BootstrapServers trong appsettings.json khi chạy local.
    /// </summary>
    public string BootstrapServers
    {
        get
        {
            return Environment.GetEnvironmentVariable("ConnectionStrings__kafka")
                ?? _bootstrapServers;
        }
        set => _bootstrapServers = value;
    }
    private string _bootstrapServers = "localhost:9092";

    /// <summary>
    /// Consumer group ID for this service
    /// </summary>
  public string ConsumerGroupId { get; set; } = "default-group";

    /// <summary>
    /// Enable auto commit for consumer
    /// </summary>
    public bool EnableAutoCommit { get; set; } = true;

    /// <summary>
    /// Auto commit interval in milliseconds
    /// </summary>
    public int AutoCommitIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Session timeout in milliseconds
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 30000;

    /// <summary>
  /// Max poll interval in milliseconds
    /// </summary>
    public int MaxPollIntervalMs { get; set; } = 300000;

    /// <summary>
    /// Auto offset reset strategy (earliest, latest, none)
    /// </summary>
    public string AutoOffsetReset { get; set; } = "earliest";

    /// <summary>
    /// Enable idempotence for producer
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Max in-flight requests per connection
    /// </summary>
    public int MaxInFlight { get; set; } = 5;

    /// <summary>
    /// Acks setting for producer (0, 1, all)
    /// </summary>
    public string Acks { get; set; } = "all";

    /// <summary>
  /// Retry backoff in milliseconds
/// </summary>
    public int RetryBackoffMs { get; set; } = 100;
}
