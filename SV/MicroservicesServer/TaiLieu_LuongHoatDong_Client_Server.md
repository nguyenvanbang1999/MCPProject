# Tài liệu Luồng Hoạt động: TestClient (Unity) ⇄ MicroservicesServer (.NET)

> **Ngày tạo:** 2026-07-02
> **Phạm vi:** Mô tả kiến trúc và luồng hoạt động end-to-end giữa game client (Unity) và backend microservices (.NET 8).
>
> - **Client:** `C:\Users\Admin\Desktop\ProjectUnity\TestClient`
> - **Server:** `C:\Project\MicroservicesServer\MicroservicesServer`

---

## 1. Tổng quan hệ thống

Hệ thống gồm **1 game client (Unity)** kết nối tới một **backend microservices** theo mô hình **event-driven**. Đặc điểm chính:

| Thành phần | Công nghệ |
|---|---|
| Giao tiếp Client ↔ Server | **TCP** (custom framing) |
| Serialize message (TCP) | **MessagePack** (nhị phân, gọn) |
| Routing message | **MurmurHash3** của tên class message → `MessageTypeId` (4 byte) |
| Giao tiếp giữa các service | **Apache Kafka** (bất đồng bộ, event-driven) |
| Serialize event (Kafka) | **JSON** |
| Lưu trữ | **MongoDB** (mỗi service 1 database) |
| Service discovery | **ServiceRegistry** (custom, không dùng Consul/Eureka) |
| Orchestration (dev) | **.NET Aspire** / **Docker Compose** (Kafka) |
| Định danh người dùng | **DeviceId** (`SystemInfo.deviceUniqueIdentifier`), **không dùng token** |
| Tự động hoá test | **MCP** (`com.coplaydev.unity-mcp`) — Claude điều khiển client |

---

## 2. Kiến trúc tổng thể

```
                          ┌──────────────────────────────────────────────────┐
                          │                  BACKEND (.NET 8)                  │
                          │                                                    │
  ┌───────────────┐  TCP  │  ┌──────────────┐        Kafka topic              │
  │  Unity Client │ :5001 │  │  GateWayTCP  │◄──── "service-registry" ────┐   │
  │  (TestClient) │◄─────►│  │  (router)    │                             │   │
  └───────────────┘       │  └──────┬───────┘                      ┌──────┴────────┐
        │                 │         │ TCP nội bộ                    │ ServiceRegistry│
        │ MCP             │         │ (theo MessageTypeId)          │ (discovery)    │
        │ (Editor)        │  ┌──────┴────────────────────┐         └────────────────┘
        ▼                 │  ▼            ▼               ▼                 ▲ HTTP /register
  ┌───────────┐           │ ┌──────────┐ ┌────────────┐ ┌─────────────┐    │
  │ Claude/MCP│           │ │AuthService│ │LevelService│ │ResourceSvc  │────┘
  │  Tools    │           │ └────┬─────┘ └─────┬──────┘ └──────┬──────┘
  └───────────┘           │      │             │               │
                          │      │  Kafka topic "user-events"  │
                          │      └──────►(UserCreatedEvent)◄────┘
                          │                                                    │
                          │  ┌──────────┐  ┌──────────┐  ┌──────────────┐      │
                          │  │ authdb   │  │ leveldb  │  │ resourcedb   │  MongoDB
                          │  └──────────┘  └──────────┘  └──────────────┘      │
                          └──────────────────────────────────────────────────┘
```

**Ý tưởng cốt lõi:** Gateway không biết trước message nào thuộc service nào. Mỗi service tự đăng ký các `MessageTypeId` mà nó xử lý với `ServiceRegistry`; Registry phát event Kafka; Gateway nghe event và cập nhật bảng định tuyến + mở kết nối TCP tới service. Nhờ vậy hệ thống **loose-coupling** và có thể thêm service mới mà không sửa Gateway.

---

## 3. Backend — Các Microservices

### 3.1. GateWayTCP (Cổng vào & Router)
**Đường dẫn:** `GateWayTCP/`
**Cổng:** `5001` (client kết nối vào đây)

- `TcpGatewayService.cs` — lắng nghe TCP :5001, nhận kết nối client.
- `MessageRouter.cs` — quản lý 3 bảng định tuyến:

| Dictionary | Kiểu | Ý nghĩa |
|---|---|---|
| `dicMessageRouter` | `uint → string` | MessageTypeId → URL service |
| `dicConnectControllerInternal` | `string → StreamConnectServiceToGateway` | URL service → kết nối TCP nội bộ |
| `dicConnectController` | `uint → StreamConnectClientGateWay` | UserId → kết nối client (sau login) |
| `dicGuestConnectController` | `string → StreamConnectClientGateWay` | DeviceId → kết nối client (trước login) |

- `ServiceRegistrationHandler.cs` — handler Kafka cho `ServiceRegisteredEvent`: cập nhật `dicMessageRouter` và mở TCP tới service.

### 3.2. ServiceRegistry (Service Discovery)
**Đường dẫn:** `ServiceRegistry/`

- `GET /mapRouter` — trả về bảng ánh xạ MessageTypeId → service hiện tại.
- `POST /register` — nhận `RegistryMessageRouterInfo` (URL + danh sách hash message), lưu lại và **publish `ServiceRegisteredEvent`** lên Kafka topic `service-registry`.
- `SevicesControl/ServiceExtentions.cs` — tiện ích để service tự đăng ký khi khởi động.

### 3.3. AuthService / AccountService (Xác thực)
**Đường dẫn:** `AuthService/` — **Database:** `authdb`, collection `Account`

- `Messages/CMLoginReviceCtrl.cs` — nhận `CMLogin`, tra cứu/khởi tạo user theo `deviceId`, trả về `SMLogin(userId)`, rồi publish 2 event Kafka: `UserCreatedEvent` (nếu user mới) và `UserLoggedInEvent`.
- `DB_Service/UserRepository.cs` — lưu trữ user.
- `DB_Service/AccountCounterService.cs` — tự tăng `userId`.

### 3.4. LevelService (Cấp độ người chơi)
**Đường dẫn:** `LevelService/` — **Database:** `leveldb`, collection `Level`

- `Events/UserCreatedEventHandler.cs` — nghe `UserCreatedEvent`, tạo `LevelDocument{ userId, currentLevel = 1 }`, gửi `SMGetLevelData` về Gateway.

### 3.5. ResourceService (Tài nguyên người chơi)
**Đường dẫn:** `ResourceService/` — **Database:** `resourcedb`, collection `Resource`

- `Events/UserCreatedEventHandler.cs` — nghe `UserCreatedEvent`, tạo `ResourceDocument{ userId, gold = 100, gem = 10 }`, gửi `SMGetResourceData` về Gateway.

### 3.6. YARPGateWay (HTTP Reverse Proxy)
**Đường dẫn:** `YARPGateWay/` — Proxy HTTP `/auth/{**catch-all}` → AuthService (`https://auth`). Dành cho các API REST (song song với TCP gateway).

### 3.7. AppHost (Aspire Orchestration)
**File:** `MicroservicesServer.AppHost/AppHost.cs`

Điều phối toàn bộ trong môi trường dev. **Thứ tự khởi động** (ràng buộc bằng `WaitFor`):

```
1. Kafka  →  2. MongoDB  →  3. ServiceRegistry
→  4. AuthService / LevelService / ResourceService (đăng ký với Registry)
→  5. GateWayTCP (khởi động cuối, sau khi mọi service đã đăng ký)
```

### 3.8. Thư viện dùng chung
- **ServiceShare/** — `KafkaEventBus.cs` (producer, idempotent, acks=all), `KafkaConsumerService.cs` (auto-discover handler qua reflection), `MongoDbExtensions.cs`, `MongoService.cs`, `IDbCollection<T>` (repository pattern).
- **SharedContracts/** — `MessageBase`, `MessageUtil` (hash + serialize), các lớp `StreamConnect*` quản lý TCP framing.

---

## 4. Client — Unity (TestClient)

### 4.1. Các thành phần chính

| Thành phần | File | Vai trò |
|---|---|---|
| Điều phối game (singleton, `DontDestroyOnLoad`) | `GameManager.cs` | `Connect()`, `PlayLevel()`, `EndLevel()`, giữ `LastLevelResult` |
| Tầng mạng TCP | `DemoTCP.cs` | Kết nối `host:port`, handshake DeviceId, gửi `CMLogin` |
| Handler login | `CMLogin.cs` (`SMLoginHandle`) | Nhận `SMLogin`, bắn event `OnLoginSuccess` |
| Scene Intro | `IntroSceneManager.cs` | Nút **Connect** → `GameManager.Connect()` |
| Scene Home | `HomeSceneManager.cs` | Hiển thị account + kết quả level trước, nút **Play** |
| Scene Level | `LevelSceneManager.cs` | Nút **Win/Lose** → set kết quả, quay về Home |
| MCP Tools | `Assets/Editor/MCP/TestClientTools.cs` | Cho Claude điều khiển client |

### 4.2. Trạng thái game (State Machine)

```
        ┌────────┐  Connect + SMLogin   ┌────────┐   Play    ┌─────────┐
        │ Intro  │ ───────────────────► │  Home  │ ────────► │  Level  │
        └────────┘                      └────────┘           └─────────┘
                                             ▲                     │
                                             │  Win / Lose (sau 1s)│
                                             └─────────────────────┘
```

`enum LevelResult { None, Win, Lose }` — lưu trong `GameManager.LastLevelResult`, hiển thị lại ở Home.

### 4.3. Cấu hình kết nối (Inspector — `DemoTCP.cs`)
- `host` (mặc định `"https:localhost"` — **cần chỉnh về host thật**)
- `port` (mặc định `"0000"` — **cần chỉnh về `5001`**)
- `idDemo = 1234`, `message` (debug)

---

## 5. Giao thức truyền thông

### 5.1. Định dạng message TCP (MessagePack)
```
[ 4 byte: MessageTypeId (uint) ][ N byte: dữ liệu MessagePack ]
```
- `MessageTypeId = MurmurHash3(tên_class_message)` — tính trong `MessageUtil.cs`.
- Gateway đọc 4 byte đầu → tra `dicMessageRouter` → route tới service tương ứng.

### 5.2. Frame protocol (`StreamConnectController.cs`)
- **Data frame:** `[0xFD][ackID(2)][length(2)][payload]`
- **Control frame:** `[0xFE][code]` với `0x01`=Ping, `0x02`=ACK
- **Tham số tin cậy:** Ping mỗi `1000ms`; ngắt sau `6` chu kỳ miss; ACK timeout `3000ms`; retry tối đa `3`; kiểm tra ACK mỗi `500ms`.

### 5.3. Handshake DeviceId
Ngay sau khi mở TCP, client gửi: `[2 byte: độ dài][DeviceId dạng UTF-8]`. Gateway lưu vào `dicGuestConnectController[deviceId]` (kết nối "khách" trước khi có userId).

---

## 6. Các loại Message & Event

### 6.1. Message TCP (MessagePack) — kế thừa `MessageBase { userId, messageId }`

| Message | Hướng | Trường chính |
|---|---|---|
| `CMLogin` | Client → Server | `[Key(2)] string deviceId` |
| `SMLogin` | Server → Client | `[Key(2)] string deviceId` (+ `userId`) |
| `SMGetLevelData` | Server → Client | `[Key(2)] LevelData levelData` |
| `LevelData` | — | `[Key(0)] int currentLevel` |
| `SMGetResourceData` | Server → Client | `[Key(2)] long Gold`, `[Key(3)] long Gem` |

### 6.2. Event Kafka (JSON)

| Event | Topic | Nội dung |
|---|---|---|
| `UserCreatedEvent` | `user-events` (key `creat_new_user`) | `{ UserId, Username, AccountType, SourceService }` |
| `UserLoggedInEvent` | `user-events` (key `user_login`) | `{ UserId, LoginTime }` |
| `ServiceRegisteredEvent` | `service-registry` | `{ ServiceUrl, ServiceName, MessageHashes[], RegisteredAt }` |

---

## 7. Luồng chi tiết (Sequence)

### 7.1. Đăng ký service (khởi động server)
```
Service (Auth/Level/Resource)      ServiceRegistry            GateWayTCP
   │  mở TCP listener (port ephemeral)   │                        │
   │  tính MurmurHash3 các message        │                        │
   │  POST /register (URL + hashes) ─────►│                        │
   │                                      │ lưu bảng router        │
   │                                      │ publish ServiceRegisteredEvent ─► (Kafka: service-registry)
   │                                      │                        │  nhận event
   │◄──────────────  Gateway mở TCP nội bộ tới service ───────────┤  cập nhật dicMessageRouter
```

### 7.2. Đăng nhập (Login)
```
Unity Client            GateWayTCP           AuthService      LevelSvc/ResourceSvc
   │  TCP connect :5001     │                    │                    │
   │  gửi DeviceId ────────►│ lưu guest conn     │                    │
   │  gửi CMLogin ─────────►│ route theo hash ──►│                    │
   │                        │                    │ tra/ tạo user (Mongo)
   │                        │                    │ publish UserCreatedEvent + UserLoggedInEvent
   │                        │◄── SMLogin(userId)─┤ (Kafka: user-events)──────────►│
   │◄── SMLogin ────────────┤                    │                    │ tạo Level=1 / Gold=100,Gem=10
   │  OnLoginSuccess        │◄── SMGetLevelData / SMGetResourceData ───────────────┤
   │◄── SMGetLevelData ─────┤                    │                    │
   │◄── SMGetResourceData ──┤                    │                    │
   │  load scene "Home"     │                    │                    │
```
> Sau login, Gateway "nâng cấp" kết nối từ `dicGuestConnectController[deviceId]` sang `dicConnectController[userId]`.

### 7.3. Chơi & kết thúc màn (Play / End Level)
```
Home scene           GameManager            Level scene
  │ nút Play ────────► PlayLevel()              │
  │                    LastLevelResult = None   │
  │                    load "Level" ───────────►│
  │                                             │ nút Win/Lose
  │                    EndLevel(result) ◄────────┤
  │◄── load "Home" (sau 1s), hiển thị kết quả ──┤
```

---

## 8. Cơ sở dữ liệu (MongoDB)

| DB | Collection | Document |
|---|---|---|
| `authdb` | `Account` | `{ _id, device_id, user_id }` |
| `leveldb` | `Level` | `{ userId, currentLevel }` |
| `resourcedb` | `Resource` | `{ userId, gold, gem }` |

---

## 9. Tích hợp MCP (tự động hoá test)

**Package:** `com.coplaydev.unity-mcp` · **File:** `Assets/Editor/MCP/TestClientTools.cs`
Chạy trên **main thread của Unity Editor**; hầu hết cần **Play Mode**.

| Tool MCP | Tham số | Hành vi |
|---|---|---|
| `game_login` | — | Nếu chưa Play Mode: vào Play Mode + load Intro. Nếu đang Play: `GameManager.Connect()` |
| `game_start_level` | — | `GameManager.PlayLevel()` → load "Level", reset kết quả về None |
| `game_end_level` | `result`: `win/won/victory` hoặc `lose/lost/defeat` | `GameManager.EndLevel(result)` → ghi kết quả, load "Home" |
| `game_status` | — | Trả về `{ is_playing, is_busy, active_scene, has_game_manager, user_id, logged_in, last_level_result }` |

> Quy trình smoke-test điển hình của Claude: `game_login` → poll `game_status` (chờ `logged_in=true`) → `game_start_level` → `game_end_level {result:"win"}` → `game_status` xác nhận `last_level_result="Win"`.

---

## 10. Hạ tầng & Cách chạy

**Docker Compose** (`docker-compose.kafka.yml`): Zookeeper (:2181), Kafka (:9092 / nội bộ :29092), Kafka-UI (:8080). Topic tự tạo khi publish lần đầu.

**Chạy server (dev):** khởi động `MicroservicesServer.AppHost` (Aspire) — tự lên Kafka, MongoDB, Registry, các service, Gateway theo đúng thứ tự.

**Chạy client:** mở project Unity `TestClient`, chỉnh `host`/`port` trong `DemoTCP` (Inspector) trỏ về Gateway `:5001`, vào Play Mode ở scene **Intro** và bấm **Connect** (hoặc dùng MCP `game_login`).

---

## 11. Bảng tra cứu file quan trọng

### Server
| Thành phần | File |
|---|---|
| Gateway TCP | `GateWayTCP/TcpGatewayService.cs`, `MessageRouter.cs`, `ServiceRegistrationHandler.cs` |
| Registry | `ServiceRegistry/Program.cs`, `SevicesControl/ServiceExtentions.cs` |
| Auth | `AuthService/Messages/CMLoginReviceCtrl.cs`, `DB_Service/UserRepository.cs` |
| Level | `LevelService/Events/UserCreatedEventHandler.cs` |
| Resource | `ResourceService/Events/UserCreatedEventHandler.cs` |
| Orchestration | `MicroservicesServer.AppHost/AppHost.cs` |
| Kafka/Mongo | `ServiceShare/KafkaEventBus.cs`, `KafkaConsumerService.cs`, `MongoDbExtensions.cs` |
| Contracts | `SharedContracts/Messages/MessageBase.cs`, `MessageUtil.cs`, `ConnectController/StreamConnectController.cs` |

### Client
| Thành phần | File |
|---|---|
| Điều phối | `GameManager.cs` |
| Mạng TCP | `DemoTCP.cs` |
| Handler login | `CMLogin.cs` |
| Scenes | `IntroSceneManager.cs`, `HomeSceneManager.cs`, `LevelSceneManager.cs` |
| MCP | `Assets/Editor/MCP/TestClientTools.cs` |

---

## 12. Ghi chú về bảo mật (hiện trạng)

- **Không có token / phiên**: định danh hoàn toàn bằng `DeviceId` → giả định mạng LAN tin cậy.
- **Không phân quyền**: mọi user đã đăng nhập truy cập được mọi service.
- Đây là mô hình phù hợp cho **môi trường test/dev**. Nếu lên production nên bổ sung: xác thực token (JWT), TLS cho TCP, và kiểm soát truy cập theo message.
