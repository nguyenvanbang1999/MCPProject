namespace ServiceShare.EventBus;

/// <summary>
/// Interface for publishing events to the event bus
/// </summary>
public interface IEventBus
{

    /// <summary>
    /// Publish an event with a specific key to the specified topic
    /// </summary>
    /// <typeparam name="TEvent">Type of event to publish</typeparam>
    /// <param name="topic">Kafka topic name</param>
    /// <param name="key">Message key for partitioning</param>
    /// <param name="event">Event data to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken cancellationToken = default);
}
