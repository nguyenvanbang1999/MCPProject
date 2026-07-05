using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ServiceShare.EventBus;

/// <summary>
/// Extension methods for configuring Kafka event bus
/// </summary>
public static class KafkaEventBusExtensions
{
    /// <summary>
    /// Add Kafka event bus (producer only — không đăng ký consumer).
    /// Dùng cho các service chỉ cần publish event (vd: ServiceRegistry).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKafkaEventBusProducerOnly(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        OptionsConfigurationServiceCollectionExtensions.Configure<KafkaSettings>(
            services,
            configuration.GetSection(KafkaSettings.SECTION_NAME));

        services.AddSingleton<IEventBus, KafkaEventBus>();
        return services;
    }

    /// <summary>
    /// Add Kafka event bus producer + consumer background service.
    /// Dùng kết hợp với AddKafkaConsumer() để subscribe topic.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKafkaEventBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        OptionsConfigurationServiceCollectionExtensions.Configure<KafkaSettings>(
            services,
            configuration.GetSection(KafkaSettings.SECTION_NAME));

        // Register event bus as singleton
        services.AddSingleton<IEventBus, KafkaEventBus>();
        return services;
    }

    /// <summary>
    /// Add Kafka event bus producer with custom settings
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureSettings">Action to configure settings</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKafkaEventBus(
        this IServiceCollection services,
        Action<KafkaSettings> configureSettings)
    {
        services.Configure(configureSettings);
        services.AddSingleton<IEventBus, KafkaEventBus>();

        return services;
    }

    /// <summary>
    /// Add Kafka consumer with event subscriptions
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>Consumer builder for adding subscriptions</returns>
    public static KafkaConsumerBuilder AddKafkaConsumer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Kafka settings using OptionsConfigurationServiceCollectionExtensions
        OptionsConfigurationServiceCollectionExtensions.Configure<KafkaSettings>(
            services,
            configuration.GetSection(KafkaSettings.SECTION_NAME));

        return new KafkaConsumerBuilder(services);
    }

    /// <summary>
    /// Add Kafka consumer with custom settings
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureSettings">Action to configure settings</param>
    /// <returns>Consumer builder for adding subscriptions</returns>
    public static KafkaConsumerBuilder AddKafkaConsumer(
        this IServiceCollection services,
        Action<KafkaSettings> configureSettings)
    {
        services.Configure(configureSettings);
        return new KafkaConsumerBuilder(services);
    }
}

/// <summary>
/// Builder for configuring Kafka consumer subscriptions
/// </summary>
public class KafkaConsumerBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<ConsumerSubscription> _subscriptions = new();

    public KafkaConsumerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Subscribe to a Kafka topic with a specific event type and handler
    /// </summary>
    /// <typeparam name="TEvent">Type of event</typeparam>
    /// <typeparam name="THandler">Type of event handler</typeparam>
    /// <param name="topic">Kafka topic name</param>
    /// <returns>Builder for chaining</returns>
    public KafkaConsumerBuilder Subscribe<TEvent, THandler>(string topic)
        where THandler : class, IEventHandler<TEvent>
    {
        _subscriptions.Add(new ConsumerSubscription
        {
            Topic = topic,
            HandlerType = typeof(THandler)
        });

        // Register the handler in DI
        _services.AddScoped<IEventHandler<TEvent>, THandler>();

        return this;
    }

    /// <summary>
    /// Build and register the consumer background service
    /// </summary>
    /// <returns>Service collection</returns>
    public IServiceCollection Build()
    {
        // Register subscriptions as singleton
        _services.AddSingleton<IEnumerable<ConsumerSubscription>>(_subscriptions);

        // Register the background service
        _services.AddHostedService<KafkaConsumerService>();

        return _services;
    }
}
