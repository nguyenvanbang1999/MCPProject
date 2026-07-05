namespace ServiceShare.EventBus;

/// <summary>
/// Interface for handling events consumed from the event bus
/// </summary>
/// <typeparam name="TEvent">Type of event to handle</typeparam>
public interface IEventHandler<in TEvent> : IEventHandler
{
    /// <summary>
    /// Handle the consumed event
    /// </summary>
    /// <param name="event">Event data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

public interface IEventHandler
{
    string Topic { get; }
}
