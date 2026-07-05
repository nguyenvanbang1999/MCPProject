# Plan triển khai: PvP Realtime Service (direct TCP, stateful)

> Quyết định đã chốt: tạo service realtime riêng, stateful. Client mở **kết nối TCP thứ hai** thẳng đến
> instance realtime trong suốt trận đấu — bộ message tương tác realtime đi qua kết nối này,
> **không qua Gateway**. Gateway chỉ phục vụ meta-game (account, resource, liveops, tìm trận).

## 1. Kiến trúc tổng quan

```
                        ┌──────────────┐
                        │ Unity client │
                        └──────┬───┬───┘
              kết nối 1 (meta) │   │ kết nối 2 (chỉ tồn tại trong trận)
                               │   │
                    ┌──────────▼┐  │
                    │ GateWayTCP│  │
                    └─────┬─────┘  │
                          │        │
               ┌──────────▼──────┐ │      ┌────────────────────┐
               │ Matchmaking     │ │      │ PvPRealtimeService │
               │ Service         │─┼─────►│ (N room / instance)│
               │ (stateless)     │ │ tạo  └─────────┬──────────┘
               └─────────────────┘ │ room + token   │
                                   └────────────────┘
                                        CMJoinMatch{token}

Kafka: MatchEnded → RewardService/StatsService; InstanceStatus → Matchmaking
```

Nguyên tắc phân chia:
- **Meta / match-scoped**: mọi message có vòng đời dài hơn 1 trận đi qua Gateway; mọi message
  chỉ có nghĩa trong 1 trận đi qua kết nối trực tiếp.
- **1 instance host N room** (room-based), KHÔNG spin 1 process cho mỗi trận. Mỗi room là một
  object độc lập trong process, có state riêng trong RAM.
- **userId trên kết nối realtime lấy từ binding sau khi join token hợp lệ** — không bao giờ
  tin `userId` nằm trong body message ở kết nối này.

## 2. Project mới

| Project | Loại | Vai trò |
|---|---|---|
| `PvPRealtime.Contracts` | Library | Bộ message realtime. **KHÔNG** đưa vào danh sách đăng ký hash với `ServiceRegistry` (để Gateway không bao giờ route nhầm). |
| `MatchmakingService` | Console (ASP.NET) | Stateless. Nhận `CMFindMatch` qua Gateway theo cơ chế hash hiện có. Chọn instance, phát join token, trả `SMMatchFound`. |
| `PvPRealtimeService` | Console (ASP.NET) | Stateful. TcpListener public, RoomManager, game loop, publish `MatchEnded`. |

Tái sử dụng từ codebase hiện có:
- `SharedContracts/ConnectController/StreamConnectControllerBinary|Message` — dùng làm khung
  framing cho cả 2 đầu của kết nối realtime (server accept + client Unity).
- `ServiceShare/EventBus` (Kafka) — cho `MatchEnded`, `InstanceStatus`.
- `ServiceRegistry.SevicesControl.ServiceExtentions.RegisterServiceAsync` — MatchmakingService
  đăng ký như service thường. PvPRealtimeService đăng ký với **danh sách hash rỗng**
  (registry đã hỗ trợ push-only service) chỉ để có mặt trong hệ thống giám sát.

## 3. Message contracts (PvPRealtime.Contracts)

```
// Client → Realtime instance
CMJoinMatch      { matchId, joinToken }              // BẮT BUỘC là message đầu tiên
CMRejoinMatch    { matchId, joinToken }              // reconnect giữa trận
CMPlayerAction   { actionType, payload, clientSeq }  // input trong trận
CMLeaveMatch     { }

// Realtime instance → Client
SMJoinMatchResult { ok, reason, matchState }
SMMatchState      { snapshot/delta, serverTick }
SMPlayerJoined / SMPlayerLeft / SMPlayerReconnected
SMMatchEnd        { result, rewardsPreview }

// Matchmaking (đi qua Gateway — đăng ký hash bình thường)
CMFindMatch      { mode }
CMCancelFindMatch{ }
SMMatchFound     { matchId, host, port, joinToken }
SMMatchCancelled { reason }

// Kafka events
MatchAssigned  { matchId, instanceId, players[{userId}] }        // log/giám sát
MatchEnded     { matchId, players[{userId, result, score}], duration }
InstanceStatus { instanceId, publicHost, publicPort, roomCount, maxRooms, timestamp }
```

## 4. Luồng nghiệp vụ chi tiết

### 4.1 Join trận
1. Client gửi `CMFindMatch` qua Gateway → MatchmakingService (route hash như hiện tại).
2. Matchmaker gom đủ người → chọn instance còn slot từ bảng `InstanceStatus` (in-memory,
   consume từ Kafka topic `realtime-instances`, instance publish mỗi 5s).
3. Matchmaker gọi HTTP nội bộ đến instance đã chọn: `POST /internal/rooms`
   `{ matchId, mode, players: [{userId, joinToken}] }`. Instance dựng room ở trạng thái
   `WaitingForPlayers`, trả 200. Nếu từ chối/timeout (2s) → chọn instance khác.
   *(Đây là control-plane, tần suất thấp — chấp nhận HTTP nội bộ; KHÔNG dùng cho input trong trận.)*
4. Matchmaker trả `SMMatchFound { matchId, host, port, joinToken }` cho từng client qua Gateway.
5. Client mở TCP đến `host:port`, bật `NoDelay`, gửi `CMJoinMatch { matchId, joinToken }`.
6. Instance validate token → bind connection ↔ (matchId, userId) → `SMJoinMatchResult`.
   Đủ người → room chuyển `Playing`. Quá `JoinTimeout` (15s) mà thiếu người → hủy room,
   báo matchmaker trả người đã join về hàng đợi.

### 4.2 Trong trận
- Mọi `CMPlayerAction` đi qua kết nối trực tiếp; room xử lý theo game loop
  (tick-based hoặc turn-based tùy mode) và broadcast `SMMatchState`.
- Kết nối lạ (không gửi `CMJoinMatch` hợp lệ trong 5s) → drop ngay.

### 4.3 Kết thúc trận
- Room là **authority duy nhất về kết quả** — client không bao giờ tự báo kết quả.
- Instance publish `MatchEnded` lên Kafka (retry đến khi publish thành công trước khi xóa room),
  gửi `SMMatchEnd` cho client, đóng kết nối, giải phóng room.
- RewardService/StatsService (hoặc tạm thời LevelService) consume `MatchEnded` để trả thưởng.

### 4.4 Reconnect
- Token gắn với `(matchId, userId)`, hiệu lực đến khi trận kết thúc.
- Client rớt mạng → connect lại, gửi `CMRejoinMatch` với token cũ → re-bind, nhận full snapshot.
- Room giữ chỗ trong `ReconnectWindow` (mặc định 60s, config theo mode); quá hạn → xử thua/bot.

## 5. Các phase triển khai

### Phase 1 — Contracts + skeleton PvPRealtimeService
- [ ] Tạo project `PvPRealtime.Contracts` (KHÔNG có handler đăng ký hash với registry).
- [ ] Tạo project `PvPRealtimeService`: TcpListener trên `Realtime:PublicPort` (config),
      `TcpClient.NoDelay = true` cho mọi connection.
- [ ] Lớp `RealtimeConnection : StreamConnectControllerBinary`: trạng thái
      `Unauthenticated → Bound(matchId, userId)`; deadline 5s cho `CMJoinMatch`.
- [ ] `RoomManager` (`ConcurrentDictionary<matchId, Room>`) + `Room` với vòng đời
      `WaitingForPlayers → Playing → Ended`; giới hạn `MaxRooms` theo config.
- [ ] `POST /internal/rooms` endpoint + validate join token (token do matchmaker sinh,
      instance chỉ cần so khớp danh sách token nhận được lúc tạo room — không cần crypto phức tạp).

### Phase 2 — MatchmakingService
- [ ] Tạo project, đăng ký `CMFindMatch`/`CMCancelFindMatch` qua `RegisterServiceAsync` hiện có.
- [ ] Hàng đợi ghép trận in-memory theo mode (đơn giản trước: FIFO đủ N người).
- [ ] Consume `InstanceStatus` từ topic `realtime-instances` → bảng instance + slot.
- [ ] Sinh joinToken: 32 byte random (Base64), lưu kèm matchId/userId để đối chiếu log.
- [ ] Flow tạo room (mục 4.1) + retry sang instance khác + `SMMatchFound`/`SMMatchCancelled`.

### Phase 3 — Client Unity (ClientTest)
- [ ] `RealtimeClient`: kết nối TCP thứ 2, cùng framing với kết nối Gateway hiện có.
- [ ] State machine: `Idle → Finding → Connecting → InMatch → Ended`; xử lý `SMMatchFound`
      → connect → `CMJoinMatch`.
- [ ] Reconnect: tự `CMRejoinMatch` khi rớt mạng trong trận.

### Phase 4 — Match end + tích hợp Kafka
- [ ] Publish `MatchEnded` (retry-until-success trước khi dispose room).
- [ ] Publish `InstanceStatus` định kỳ 5s (BackgroundService).
- [ ] Consumer mẫu cho `MatchEnded` (stub reward) — nhớ đăng ký bằng
      `AddKafkaConsumer(...).Subscribe(...)` (tránh lặp lại bug LevelService trước đây).

### Phase 5 — Hardening
- [ ] Config địa chỉ public riêng (`Realtime:PublicHost`) — KHÔNG dùng
      `Dns.GetHostAddresses` như `ServiceExtentions` (đó là IP nội bộ cho Gateway).
- [ ] Join timeout, reconnect window, max rooms — đưa hết vào config.
- [ ] Load test: N room song song trên 1 instance, đo tick latency.
- [ ] Chạy 2+ instance PvPRealtimeService trong AppHost, xác nhận matchmaker phân bổ đúng slot.

## 6. Thay đổi AppHost / cấu hình
- Thêm 2 project vào `MicroservicesServer.AppHost` (tham chiếu + `AddProject`).
- `PvPRealtimeService` cần port TCP cố định expose ra ngoài (khác port HTTP nội bộ);
  dev: localhost, production: mở port range trên firewall/NAT.

## 7. Rủi ro & giảm thiểu

| Rủi ro | Giảm thiểu |
|---|---|
| Instance chết giữa trận (state RAM mất) | Chấp nhận ở v1: client nhận disconnect → quay về Gateway; matchmaker nhận thiếu heartbeat `InstanceStatus` → loại instance. KHÔNG làm state recovery ở giai đoạn này. |
| Kết nối rác / scan port | Drop connection không join hợp lệ trong 5s; rate-limit accept. |
| TCP head-of-line blocking với gameplay nhịp cao | Chấp nhận với turn-based/tick vừa. Nếu cần nhịp cao: thay transport của riêng kết nối realtime bằng UDP (LiteNetLib/ENet) — kiến trúc matchmaker/token/room giữ nguyên. |
| Client gửi message meta qua kết nối realtime (hoặc ngược lại) | Hai bộ contract tách biệt; instance chỉ deserialize message thuộc `PvPRealtime.Contracts`, còn lại drop + log. |

## 8. Definition of done
- 2 client ghép trận, đánh hết 1 trận hoàn toàn qua kết nối trực tiếp; log Gateway không có
  message in-match nào.
- Kill Gateway giữa trận → trận vẫn tiếp tục (chứng minh cô lập); client quay lại meta được
  sau khi Gateway sống lại.
- Rớt mạng 1 client → rejoin trong reconnect window, nhận đúng snapshot.
- `MatchEnded` được consumer nhận đúng 1 lần; room và connection được giải phóng (không leak).
