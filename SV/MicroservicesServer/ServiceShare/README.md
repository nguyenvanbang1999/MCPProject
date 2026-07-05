# ServiceShare - Kafka Event Bus

Th? vi?n chia s? cho các microservices v?i tích h?p Kafka event bus.

## Tính n?ng

- ? Kafka producer và consumer
- ? MessagePack serialization
- ? Type-safe event handlers
- ? Dependency injection integration
- ? Background service cho consumer
- ? Logging và error handling

## Cài ??t

### 1. Thêm reference vào project

```xml
<ProjectReference Include="..\ServiceShare\ServiceShare.csproj" />
```

### 2. C?u hình Kafka trong appsettings.json

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ConsumerGroupId": "auth-service-group",
    "EnableAutoCommit": true,
    "AutoCommitIntervalMs": 5000,
    "AutoOffsetReset": "earliest",
    "EnableIdempotence": true,
    "Acks": "all"
  }
}
```

## S? d?ng Producer (Publish Events)

### 1. ??ng ký trong Program.cs

```csharp
using ServiceShare.EventBus;

var builder = WebApplication.CreateBuilder(args);

// Thêm Kafka event bus
builder.Services.AddKafkaEventBus(builder.Configuration);
```

### 2. Inject và s? d?ng IEventBus

```csharp
public class LoginService
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<LoginService> _logger;

    public LoginService(IEventBus eventBus, ILogger<LoginService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task HandleLoginAsync(string userId, string username)
    {
        // Business logic...

      // Publish event
      var loginEvent = new UserLoggedInEvent
   {
          UserId = userId,
     Username = username,
          IpAddress = "192.168.1.1",
            DeviceInfo = "Unity Client",
      SourceService = "AuthService"
     };

        await _eventBus.PublishAsync("user-events", loginEvent);
        _logger.LogInformation("Published login event for user: {Username}", username);
    }
}
```

## S? d?ng Consumer (Subscribe to Events)

### 1. T?o Event Handler

```csharp
using ServiceShare.EventBus;
using ServiceShare.Events;

public class UserLoggedInHandler : IEventHandler<UserLoggedInEvent>
{
    private readonly ILogger<UserLoggedInHandler> _logger;

    public UserLoggedInHandler(ILogger<UserLoggedInHandler> logger)
    {
     _logger = logger;
    }

    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User logged in: UserId={UserId}, Username={Username}, IP={IpAddress}",
 @event.UserId, @event.Username, @event.IpAddress);

      // X? lý business logic t?i ?ây
 // Ví d?: C?p nh?t analytics, g?i notification, etc.

 await Task.CompletedTask;
    }
}
```

### 2. ??ng ký Consumer trong Program.cs

```csharp
using ServiceShare.EventBus;
using ServiceShare.Events;

var builder = WebApplication.CreateBuilder(args);

// Thêm Kafka consumer
builder.Services.AddKafkaConsumer(builder.Configuration)
    .Subscribe<UserLoggedInEvent, UserLoggedInHandler>("user-events")
    .Subscribe<UserCreatedEvent, UserCreatedHandler>("user-events")
    .Build();
```

## T?o Custom Events

```csharp
using MessagePack;
using ServiceShare.EventBus;

[MessagePackObject]
public class GameStartedEvent : EventBase
{
    [Key(3)]
    public string GameRoomId { get; set; } = string.Empty;

    [Key(4)]
    public int PlayerCount { get; set; }

    [Key(5)]
    public string GameMode { get; set; } = string.Empty;
}
```

## Kafka Topics Convention

- `user-events` - User authentication và lifecycle events
- `game-events` - Game room và gameplay events
- `payment-events` - Payment và transaction events
- `notification-events` - Notification events

## Best Practices

1. **Event Naming**: Dùng past tense (UserLoggedIn, GameStarted, PaymentCompleted)
2. **Topic Design**: Group related events vào cùng topic
3. **Idempotency**: X? lý duplicate events gracefully
4. **Error Handling**: Log errors nh?ng không throw exception trong handler
5. **Async Processing**: T?t c? handlers nên async
6. **Serialization**: Dùng MessagePack attributes cho t?t c? properties

## Troubleshooting

### Kafka không connect ???c

```bash
# Ki?m tra Kafka ?ang ch?y
docker ps | grep kafka

# Xem logs
docker logs kafka-container
```

### Consumer không nh?n messages

- Ki?m tra `ConsumerGroupId` có ?úng không
- Ki?m tra `AutoOffsetReset` setting
- Xem logs ?? debug

### Serialization errors

- ??m b?o t?t c? properties có `[Key(n)]` attribute
- ??m b?o event class có `[MessagePackObject]` attribute
- Key numbers ph?i unique và sequential

## Examples trong Workspace

Xem các ví d? c? th?:
- `AuthService` - Producer example
- `GateWayTCP` - Consumer example
- `ServiceShare/Events/AuthEvents.cs` - Event definitions
