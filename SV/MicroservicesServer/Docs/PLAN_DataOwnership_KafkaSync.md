# Plan triển khai: Sở hữu dữ liệu + đồng bộ bản sao qua Kafka + HTTP reconcile

> Quyết định đã chốt:
> 1. Mỗi loại dữ liệu chỉ được **ghi ở đúng 1 service chủ** (single writer — ví dụ resource/energy
>    thuộc `ResourceService`).
> 2. Service khác cần dữ liệu thì giữ **bản sao (read model)** và đồng bộ qua **Kafka**.
> 3. Có **HTTP snapshot API** ở service chủ để bản sao **cập nhật lại ngay lập tức khi phát hiện
>    sai sót** (reconciliation on demand) — HTTP không dùng cho luồng nghiệp vụ thường ngày.

## 1. Nguyên tắc bất biến

- Bản sao chỉ được ghi bởi consumer Kafka + reconcile — **không service nào tự sửa bản sao theo
  logic nghiệp vụ của mình**. Mọi thay đổi số dư thật đều phải đi qua service chủ.
- Hành động **tiêu** tài nguyên (trừ energy khi start màn…) là lệnh gửi về service chủ, service chủ
  trừ **nguyên tử** rồi phát event kết quả. Check trên bản sao chỉ là soft-check (chặn sớm cho UX).
- Event mang **số dư tuyệt đối + version tăng dần**, không mang delta — mất 1 event thì event kế
  tiếp tự sửa bản sao; version gap là tín hiệu phát hiện sai sót.

## 2. Chuẩn event & versioning

```
ResourceChanged {
  userId, resourceType,            // ví dụ "energy", "gold"
  balance,                         // SỐ DƯ TUYỆT ĐỐI sau thay đổi
  version,                         // long, tăng 1 mỗi lần ghi, theo (userId, resourceType)
  sourceEventId,                   // id duy nhất của lần ghi (chống trùng)
  reason, occurredAt
}
PurchaseCompleted { purchaseId, userId, items[{resourceType, amount}], occurredAt }
SpendResourceRequested { requestId, userId, resourceType, amount, reason }   // command
SpendResourceResult    { requestId, ok, balance, version, failReason }
```

- **Partition key = userId** để thứ tự event của mỗi user được bảo toàn.
  - [ ] Task: kiểm tra `ServiceShare/EventBus/KafkaEventBus` đã set message key khi produce chưa;
        nếu chưa → thêm overload `PublishAsync(topic, key, event)`.
- Version lưu ngay trong document resource của service chủ, tăng trong cùng lệnh
  `findOneAndUpdate` (`$inc: {version: 1}`) — nguyên tử, không cần transaction.

## 3. Hạ tầng dùng chung mới (đặt trong `ServiceShare`)

| Thành phần | Mô tả |
|---|---|
| `Outbox/OutboxStore` + `OutboxPublisherService` | Writer lưu event vào collection `outbox` cùng nhịp ghi nghiệp vụ; BackgroundService quét → publish Kafka → đánh dấu sent. Chống mất event khi crash giữa "ghi DB" và "publish". |
| `Idempotency/ProcessedEventStore` | Collection `processed_events` (unique index theo `sourceEventId`/`purchaseId`/`requestId`). Consumer check-trước-ghi-sau → event đến 2 lần không áp dụng 2 lần. |
| `ReadModel/ReplicaStore` | Upsert có guard version: chỉ ghi đè khi `event.version > local.version`. Collection mẫu: `resource_replica { userId, resourceType, balance, version, updatedAt }`. |
| `Reconcile/ReconciliationClient` | HTTP client gọi snapshot API của service chủ, ghi đè bản sao (kèm version). Đây là cơ chế "update ngay lập tức khi phát hiện sai sót" đã chốt. |

## 4. HTTP snapshot API (chỉ ở service chủ, chỉ để reconcile)

```
GET /internal/resources/{userId}/snapshot
→ { items: [{ resourceType, balance, version }], asOf }
```

Bản sao gọi API này khi (đây là các "trigger phát hiện sai sót"):
1. **Bootstrap**: service khởi động / gặp userId lần đầu chưa có trong replica.
2. **Version gap**: nhận event có `version > local.version + 1` → đã lỡ event ở giữa → reconcile
   ngay userId đó rồi mới xử lý tiếp.
3. **Staleness nghi ngờ**: có pending spend quá `N` giây không thấy `SpendResourceResult`.
4. **Bất thường nghiệp vụ**: bản sao ra số âm, hoặc kết quả lệnh trả về mâu thuẫn với bản sao.
5. **Định kỳ (tùy chọn, phase sau)**: job so khớp ngẫu nhiên M user/phút, log lệch để giám sát.

URL service chủ lấy qua Aspire service discovery (config `services:resourceservice:...`,
giống cách `LevelService/Program.cs` đang lấy URL Registry).

## 5. Áp dụng 1 — Shop mua gói → cộng resource

```
Client ─CMBuyPackage→ Gateway → ShopService
ShopService: validate + lưu đơn + outbox(PurchaseCompleted)   [cùng nhịp ghi]
          → trả SMBuySuccess ngay (đơn đã ghi nhận)
Outbox → Kafka → ResourceService:
          idempotent theo purchaseId → cộng resource ($inc + $inc version) → outbox(ResourceChanged)
ResourceChanged → các replica cập nhật
ResourceService → IGatewaySender đẩy SMResourceUpdate về client (số dư mới cho UI)
```

- [ ] ShopService (khi có): lưu đơn + outbox.
- [ ] ResourceService: consumer `PurchaseCompleted` idempotent; **nhớ đăng ký bằng
      `AddKafkaConsumer(...).Subscribe(...)`** (bug "quên đăng ký consumer" từng xảy ra ở
      LevelService — xem comment trong `LevelService/Program.cs`).
- [ ] Push `SMResourceUpdate` qua `IGatewaySender` sau khi cộng xong.

## 6. Áp dụng 2 — LevelService check energy khi start màn

```
CMStartLevel → LevelService:
  1. Soft-check trên bản sao: effective = replica.balance − Σ(pending spends của user)
     → thiếu: SMStartLevelFailed (chặn sớm, không gửi lệnh)
  2. Đủ: ghi pending spend { requestId = sessionId, amount } rồi publish SpendResourceRequested
  3. Nhận SpendResourceResult (khớp requestId):
     - ok  → xóa pending, tạo session màn chơi, SMStartLevel cho client
     - fail→ xóa pending, SMStartLevelFailed, reconcile userId đó ngay (trigger 4)
```

- `ResourceService` xử lý `SpendResourceRequested`: idempotent theo `requestId`, trừ nguyên tử
  `findOneAndUpdate({userId, type, balance: {$gte: amount}}, {$inc: {balance: -amount, version: 1}})`
  → phát `SpendResourceResult` + `ResourceChanged` (qua outbox).
- **Pending tracking là bắt buộc** để tránh bẫy "hồi sinh energy ảo": event `ResourceChanged`
  mang số dư tuyệt đối chưa phản ánh lệnh đang bay sẽ ghi đè bản sao; mọi phép check phải trừ
  thêm các khoản pending. Pending lưu in-memory + TTL (quá `N` giây → trigger reconcile số 3).
- Chính sách đã bàn: nếu quyết định cho start lạc quan trước khi có kết quả lệnh (bỏ bước chờ ở
  mục 3), phải chốt hành xử khi lệnh fail sau khi trận đã start — khuyến nghị với energy: **cho nợ,
  kẹp số dư về 0**, không hủy trận. Ghi rõ lựa chọn vào config trước khi code.

## 7. Các phase triển khai

### Phase 1 — Nền tảng ServiceShare
- [ ] `OutboxStore` + `OutboxPublisherService` (BackgroundService, quét theo batch, retry).
- [ ] `ProcessedEventStore` (unique index, helper `TryMarkProcessedAsync`).
- [ ] `ReplicaStore` với version-guard upsert.
- [ ] `KafkaEventBus`: produce có partition key theo userId.

### Phase 2 — ResourceService thành service chủ chuẩn
- [ ] Thêm `version` vào document resource; mọi lệnh ghi `$inc version` nguyên tử.
- [ ] Outbox cho `ResourceChanged` ở mọi đường ghi.
- [ ] `GET /internal/resources/{userId}/snapshot`.
- [ ] Consumer `SpendResourceRequested` (idempotent) → `SpendResourceResult`.
- [ ] Push `SMResourceUpdate` cho client qua `IGatewaySender` sau mỗi thay đổi.

### Phase 3 — Bản sao energy ở LevelService
- [ ] Consumer `ResourceChanged` → `ReplicaStore` (filter `resourceType == "energy"`).
- [ ] `ReconciliationClient` + đủ 4 trigger (bootstrap, version gap, pending timeout, bất thường).
- [ ] Bootstrap lần đầu bằng snapshot API.

### Phase 4 — Luồng start màn end-to-end
- [ ] Pending spend tracking + soft-check.
- [ ] `SpendResourceRequested/Result` flow + tạo session màn chơi.
- [ ] Chốt và implement chính sách khi lệnh fail (mục 6).

### Phase 5 — Shop flow + giám sát
- [ ] `PurchaseCompleted` flow (mục 5) khi ShopService ra đời.
- [ ] Metrics/log: consumer lag, số lần reconcile theo trigger, số version gap, độ lệch phát hiện
      được — reconcile nhiều bất thường nghĩa là đang có bug ở producer/consumer, phải điều tra.

## 8. Kịch bản test bắt buộc (definition of done)

| Kịch bản | Kỳ vọng |
|---|---|
| Kill ResourceService ngay sau khi ghi đơn nhưng trước khi publish | Outbox publish lại sau khi sống dậy — không mất event |
| Replay cùng 1 event 2 lần (giả lập at-least-once) | Số dư chỉ đổi 1 lần (idempotent) |
| Xóa tay 1 event khỏi luồng (giả lập gap) | Consumer phát hiện version gap → tự reconcile qua HTTP → bản sao đúng lại ngay |
| 2 `CMStartLevel` song song cùng user | Chỉ 1 lệnh spend thắng; trận thứ hai bị từ chối |
| Sửa tay bản sao cho lệch rồi chờ trigger | Reconcile phục hồi đúng số dư + log cảnh báo |
| Consumer restart giữa backlog | Xử lý tiếp không mất, không đúp (offset + idempotency) |

## 9. Rủi ro & lưu ý

- **Đừng thêm writer thứ hai "cho tiện"** — mọi vi phạm single-writer sẽ làm version vô nghĩa
  và bản sao lệch vĩnh viễn. Nếu một service khác cần ghi resource, nó gửi command về chủ.
- Mongo transaction đa document cần replica set; thiết kế trên cố tình chỉ dùng thao tác
  **1 document nguyên tử** (`findOneAndUpdate` + outbox là collection riêng, chấp nhận
  publish-sau-crash nhờ retry + idempotency) để không phụ thuộc cấu hình Mongo.
- Tài nguyên gắn tiền thật (gem, vé nạp): **không dùng soft-check lạc quan** — bắt buộc chờ
  `SpendResourceResult` trước khi trả hàng, dù UX chậm hơn một nhịp.
- HTTP snapshot chỉ để reconcile — nếu thấy code nghiệp vụ nào gọi snapshot trong luồng thường
  ngày thì thiết kế đang bị dùng sai, cần review lại.
