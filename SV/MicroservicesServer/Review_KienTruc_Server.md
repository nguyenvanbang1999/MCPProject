# Review Kiến trúc — MicroservicesServer

> **Ngày:** 2026-07-03
> **Phương pháp:** Đánh giá dựa trên truy vấn ngữ nghĩa của **MCP AI Server for Visual Studio (Roslyn)** — `GetSolutionTree`, `GetInheritance`, `FindSymbolUsages`, `GetMethodCalls`, `GetDiagnostics` — thay vì suy luận từ text. Mọi phát hiện đều kèm bằng chứng `file:line`.
> **Phạm vi:** 12 project trong `MicroservicesServer.sln`.

---

## 1. Bản đồ project (GetSolutionTree)

| Nhóm | Project | Vai trò |
|---|---|---|
| **Host** | `MicroservicesServer.AppHost`, `MicroservicesServer.ServiceDefaults` | Aspire orchestration + OTEL/health defaults |
| **Hạ tầng dùng chung** | `ServiceShare` | Kafka EventBus (producer/consumer) + MongoDB (`IDbCollection<T>`) |
| **Contracts** | `SharedContracts`, `AccountService.Contracts`, `LevelService.Contracts`, `ResourceService.Contracts` | Message types + framing + event DTO |
| **Discovery / Gateway** | `ServiceRegistry`, `GateWayTCP` | Đăng ký service + định tuyến TCP |
| **Nghiệp vụ** | `AccountService` (thư mục `AuthService/`), `LevelService`, `ResourceService` | Auth / Level / Resource |

> Lưu ý: thư mục `AuthService/` biên dịch ra assembly **`AccountService`** — dễ gây nhầm lẫn khi đọc log/điều hướng.

---

## 2. Bốn trục kiến trúc (Roslyn `GetInheritance`)

### 2.1. Message (`MessageBase` → 4 loại)
| Message | Hướng | Ghi chú |
|---|---|---|
| `CMLogin` | Client → Server | **Message client duy nhất** được một service xử lý |
| `SMLogin` | Server → Client | phản hồi đăng nhập |
| `SMGetLevelData` | Server → Client | push cấp độ |
| `SMGetResourceData` | Server → Client | push tài nguyên |

### 2.2. Handler message (`MessageReviceController<>`)
- `CMLogin → CMLoginReviceCtrl` — **AccountService**
- `SMLogin → SMLoginReviceController` — **Gateway**
- `SMGetLevelData → SMGetLevelDataReviceController` — **Gateway**
- `SMGetResourceData → SMGetResourceDataReviceController` — **Gateway**

### 2.3. Event handler Kafka (`IEventHandler<>`)
- `ServiceRegisteredEvent → ServiceRegistrationHandler` — **Gateway**
- `UserCreatedEvent → UserCreatedEventHandler` — **LevelService + ResourceService** (động cơ push của 2 service này)

### 2.4. Tầng kết nối (`StreamConnectController<Message>`)
`StreamConnectControllerMessage`, `StreamConnectControllerBinary`, `StreamConnectClientGateWay`, `StreamConnectServiceToGateway` — framing `0xFD/0xFE` + heartbeat + ACK/retry.

> **Kết luận trục kiến trúc:** `LevelService`/`ResourceService` **không xử lý message client nào** — chúng thuần *push* dựa trên `UserCreatedEvent`. Đây là cơ sở cho bản vá đăng ký (chỉ đăng ký message type có handler).

---

## 3. Điểm mạnh
- **Tách biệt tốt:** contracts độc lập, service tách rời, event-driven giảm coupling.
- **Abstraction hạ tầng:** `IDbCollection<T>` cho phép thay store; Kafka producer idempotent, `acks=all`.
- **Protocol tự thân chỉn chu:** framing rõ ràng, heartbeat phát hiện chết kết nối, ACK + retry cho message quan trọng.
- **Aspire:** orchestration + health check + OTEL sẵn có, thứ tự khởi động bằng `WaitFor`.

---

## 4. Vấn đề & rủi ro (kèm bằng chứng vs-mcp)

### 🔴 Cao

#### H1 — Pattern handler xung khắc với Dependency Injection
`GetMethodCalls(MessageUtil.OnReviceMessage)` cho thấy handler được tạo bằng **`Activator.CreateInstance`** (constructor rỗng) tại `SharedContracts/Messages/MessageUtil.cs:256`, rồi gọi `OnReveive`.
Hệ quả trực tiếp — `GetDiagnostics(AccountService)`:
```
CMLoginReviceCtrl.cs:14 → CS0649: Field '_eventBus' is never assigned, always null
```
→ Constructor injection **không chạy** cho handler, nên không thể inject service sạch sẽ. Đồng thời **tạo instance + reflection mỗi message** = tốn allocation trên hot path.
**Khuyến nghị:** truyền `IServiceProvider`/factory vào cơ chế dispatch để resolve handler (có DI), và cache delegate thay vì reflection mỗi lần.

#### H2 — `async void RegisterService` (fire-and-forget)
`FindSymbols` xác nhận `ServiceRegistry/SevicesControl/ServiceExtentions.cs:32` là **`async void`**. Exception khi đăng ký sẽ mất hút, caller không await/không retry được.
**Khuyến nghị:** đổi sang `async Task` + await + retry ở startup.

#### H3 — ServiceRegistry giữ state trong RAM = SPOF
`dicMessageRouter` / `dicServices` là `static` trong process (`ServiceRegistry/Program.cs`). Registry restart → **mất toàn bộ đăng ký**; các service đang chạy không tự đăng ký lại.
> Đây chính là sự cố đã gặp khi AppHost dừng: toàn bộ mapping biến mất.
**Khuyến nghị:** persist (Redis/DB) **hoặc** cho service heartbeat/re-register định kỳ.

### 🟡 Trung bình

#### M1 — Static dùng chung `connectToGateway`
`FindSymbolUsages(connectToGateway)` = **8 usage**: set trong `ServiceExtentions` (dòng 104, 127), dùng bởi `UserCreatedEventHandler` (Level: 58,64 / Resource: 53,59) và `CMLoginReviceCtrl` (41,47).
→ Một **kết nối tĩnh duy nhất / 1 process**: có race khi Gateway reconnect (gán `null` rồi gán lại), khó test, không cô lập theo request.
**Khuyến nghị:** bọc sau abstraction `IGatewaySender` (khóa + kiểm tra null tập trung, mock được khi test).

#### M2 — Nuốt exception hàng loạt
`GetDiagnostics` bắt nhiều **CS0168 (`ex` khai báo nhưng không dùng)**:
- `GateWayTCP/TcpGatewayService.cs:105`
- `ServiceShare/EventBus/KafkaEventBus.cs:83` và `:90`

Kết hợp với các `catch { }` rải rác → lỗi bị che, khó chẩn đoán vận hành.
**Khuyến nghị:** log ở mọi `catch` (tối thiểu `Debug.LogError(ex)`).

#### M3 — Bảo mật
Không token/authN/authZ, TCP không TLS, định danh chỉ bằng `DeviceId` (giả định LAN tin cậy). Chấp nhận được ở dev; cần bổ sung trước production (JWT, TLS, kiểm soát truy cập theo message).

### 🟢 Thấp
- **CS8600** null→non-nullable: `GateWayTCP/StreamConnectClientGateWay.cs:48, 54`.
- **CS0105** using `Confluent.Kafka` trùng: `ServiceShare/EventBus/KafkaConsumerService.cs:2`.
- **Đặt tên:** `Revice` → `Receive`, `Extentions` → `Extensions`; thư mục `AuthService` vs assembly `AccountService`.

---

## 5. Khuyến nghị theo thứ tự ưu tiên

| # | Ưu tiên | Việc | Vấn đề |
|---|---|---|---|
| 1 | 🔴 | Cho handler resolve qua `IServiceProvider`/factory, cache delegate (bỏ `Activator.CreateInstance` rỗng + reflection per-message) | H1 |
| 2 | 🔴 | Persist registry **hoặc** service re-register định kỳ (chịu được restart) | H3 |
| 3 | 🔴 | `RegisterService`: `async void` → `async Task` + await/retry | H2 |
| 4 | 🟡 | Log exception trong mọi `catch` | M2 |
| 5 | 🟡 | Bọc `connectToGateway` sau `IGatewaySender` | M1 |
| 6 | 🟢 | Dọn warning CS8600/CS0105 + chuẩn hóa tên | Thấp |

---

## Phụ lục — Bằng chứng vs-mcp đã dùng
- `GetSolutionTree` → 12 project, phân nhóm ở §1.
- `GetInheritance` cho `MessageBase`, `MessageReviceController`, `IEventHandler`, `StreamConnectController` → §2.
- `FindSymbolUsages(connectToGateway)` = 8 usage → M1.
- `FindSymbolUsages(dicMessageRouter)` = 2 usage (Program.cs) → H3.
- `GetMethodCalls(MessageUtil.OnReviceMessage)` → `Activator.CreateInstance` (H1).
- `GetDiagnostics` (Warning) cho `GateWayTCP`, `ServiceShare`, `AccountService` → M2, H1, mục Thấp.
