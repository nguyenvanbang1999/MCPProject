# H??ng d?n Setup và Test Kafka Event Bus

## B??c 1: Kh?i ??ng Kafka

### S? d?ng Docker Compose

```bash
# Kh?i ??ng Kafka, Zookeeper và Kafka UI
docker-compose -f docker-compose.kafka.yml up -d

# Ki?m tra containers ?ang ch?y
docker ps

# Xem logs
docker logs kafka
docker logs zookeeper
```

### Truy c?p Kafka UI

M? browser: http://localhost:8080

T?i ?ây b?n có th?:
- Xem danh sách topics
- Monitor messages
- Xem consumer groups
- Ki?m tra partitions và offsets

## B??c 2: Build Projects

```bash
# Build toàn b? solution
dotnet build

# Ho?c build t?ng project
dotnet build ServiceShare/ServiceShare.csproj
dotnet build AuthService/AuthService.csproj
dotnet build GateWayTCP/GateWayTCP.csproj
```

## B??c 3: Ch?y Services

### Terminal 1 - AuthService
```bash
cd AuthService
dotnet run
```

### Terminal 2 - GateWayTCP
```bash
cd GateWayTCP
dotnet run
```

### Terminal 3 - ServiceRegistry (n?u c?n)
```bash
cd ServiceRegistry
dotnet run
```

## B??c 4: Test Event Publishing

### Option 1: G?i login request t? Unity Client

Khi client g?i CMLogin message, AuthService s? t? ??ng:
1. X? lý login
2. Publish `UserLoggedInEvent` ho?c `UserCreatedEvent` lên Kafka
3. GateWayTCP consumer s? nh?n và x? lý events

### Option 2: Ki?m tra logs

**AuthService logs:**
```
AuthService: Published UserCreatedEvent for user: 12345
AuthService: Published UserLoggedInEvent for user: 12345
```

**GateWayTCP logs:**
```
[Gateway] New user created - UserId: 12345, Username: device123, AccountType: Guest
[Gateway] User logged in - UserId: 12345, Username: device123, IP: N/A
```

### Option 3: S? d?ng Kafka UI

1. M? http://localhost:8080
2. Click vào topic `user-events`
3. Tab "Messages" - xem các messages ?ã publish
4. Ki?m tra:
   - Key: EventId
   - Value: Serialized MessagePack data
   - Timestamp
- Partition/Offset

## B??c 5: Monitor Consumer Groups

### Kafka UI
- Topics ? user-events ? Consumer Groups
- Xem `auth-service-group` và `gateway-service-group`
- Ki?m tra lag (s? messages ch?a consume)

### Command Line
```bash
# List consumer groups
docker exec kafka kafka-consumer-groups --bootstrap-server localhost:9092 --list

# Describe consumer group
docker exec kafka kafka-consumer-groups --bootstrap-server localhost:9092 --group gateway-service-group --describe
```

## Troubleshooting

### Kafka không k?t n?i ???c

```bash
# Restart containers
docker-compose -f docker-compose.kafka.yml restart

# Xem logs chi ti?t
docker logs kafka -f
```

### Consumer không nh?n messages

1. Ki?m tra consumer group ID trong appsettings.json
2. Ki?m tra topic name ?úng ch?a
3. Xem logs ?? tìm errors
4. Reset consumer offset:

```bash
docker exec kafka kafka-consumer-groups \
  --bootstrap-server localhost:9092 \
  --group gateway-service-group \
  --topic user-events \
  --reset-offsets --to-earliest --execute
```

### Serialization errors

- ??m b?o event class có `[MessagePackObject]` attribute
- T?t c? properties có `[Key(n)]` attribute
- Key numbers ph?i unique và tu?n t? (0, 1, 2, 3...)

## Testing Checklist

- [ ] Kafka containers ?ang ch?y
- [ ] Kafka UI accessible (http://localhost:8080)
- [ ] Topic `user-events` ???c t?o t? ??ng
- [ ] AuthService publish events thành công
- [ ] GateWayTCP consume events thành công
- [ ] Logs hi?n th? ?úng thông tin
- [ ] Không có error trong logs
- [ ] Consumer groups active trong Kafka UI

## Performance Tips

1. **Enable batch processing**: T?ng `AutoCommitIntervalMs` ?? commit theo batch
2. **Adjust consumer threads**: T?ng s? partitions cho topic ?? có th? có nhi?u consumers
3. **Monitor lag**: Gi? consumer lag < 1000 messages
4. **Use compression**: Enable compression trong producer config

## Next Steps

1. Thêm more event types (UserLoggedOut, UserUpdated, etc.)
2. Implement event handlers v?i business logic th?c t?
3. Add retry logic cho failed events
4. Implement dead letter queue
5. Add metrics và monitoring (Prometheus/Grafana)
6. Setup Kafka cluster cho production

## Additional Resources

- Confluent Kafka Docs: https://docs.confluent.io/
- MessagePack C#: https://github.com/MessagePack-CSharp/MessagePack-CSharp
- Kafka UI: https://github.com/provectus/kafka-ui
