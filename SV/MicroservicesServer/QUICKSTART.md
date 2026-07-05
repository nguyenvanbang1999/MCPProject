# Kafka Event Bus - Quick Start

## ?? Ch?y trong 5 phút

### 1. Start Kafka (1 phút)
```bash
docker-compose -f docker-compose.kafka.yml up -d
```

Verify: http://localhost:8080 (Kafka UI)

### 2. Run Services (2 phút)

**Terminal 1:**
```bash
cd AuthService
dotnet run
```

**Terminal 2:**
```bash
cd GateWayTCP
dotnet run
```

### 3. Test (2 phút)

Connect Unity client và send login request. B?n s? th?y logs:

**AuthService:**
```
AuthService: Published UserLoggedInEvent for user: 12345
```

**GateWayTCP:**
```
[Gateway] User logged in - UserId: 12345, Username: device123
```

**Kafka UI:** http://localhost:8080 ? Topics ? user-events

## ?? Publish Event (Producer)

```csharp
public class MyService
{
    private readonly IEventBus _eventBus;

 public MyService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task DoSomethingAsync()
    {
        var myEvent = new UserLoggedInEvent
        {
        UserId = "123",
            Username = "test",
    SourceService = "MyService"
        };

   await _eventBus.PublishAsync("user-events", myEvent);
    }
}
```

## ?? Handle Event (Consumer)

**1. Create Handler:**
```csharp
public class MyEventHandler : IEventHandler<UserLoggedInEvent>
{
    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken ct)
    {
      Console.WriteLine($"Received: {@event.UserId}");
        await Task.CompletedTask;
    }
}
```

**2. Register in Program.cs:**
```csharp
builder.Services.AddKafkaConsumer(builder.Configuration)
  .Subscribe<UserLoggedInEvent, MyEventHandler>("user-events")
    .Build();
```

## ? That's it!

- ? Type-safe events
- ? Async/await support
- ? Auto serialization
- ? DI integration
- ? Logging built-in

## ?? More Info

- [Full Documentation](ServiceShare/README.md)
- [Setup Guide](KAFKA_SETUP_GUIDE.md)
- [Implementation Summary](IMPLEMENTATION_SUMMARY.md)
