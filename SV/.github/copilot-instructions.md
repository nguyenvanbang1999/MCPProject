# Hướng dẫn cho GitHub Copilot (.NET)

##Copilot-Chat
- Luôn trả lời bằng tiếng Việt.

## Ngôn ngữ và phong cách
- Viết code bằng **C#**.
- Dùng **tiếng Anh** cho tên biến, hàm, class, và comment.
- Giữ code **ngắn gọn, dễ đọc**, chia nhỏ function khi cần.

---

## Chuẩn đặt tên
- **Class / Struct / Enum:** PascalCase  
  → Ví dụ: `PlayerController`, `AuthService`, `GameRoomState`
- **Biến / Field / Tham số:** camelCase  
  → Ví dụ: `playerHealth`, `networkClient`, `maxSpeed`
- **Hằng số:** ALL_CAPS_WITH_UNDERSCORES  
  → Ví dụ: `MAX_PLAYER_COUNT`
- **Private field:** có tiền tố `_` (ví dụ `_score`, `_playerId`)
- **Event:** PascalCase + hậu tố `Event`  
  → Ví dụ: `OnPlayerJoinedEvent`

---


## Quy tắc lập trình .NET (Server, Tool, API)
- Sử dụng **ASP.NET Core / Minimal API / BackgroundService** nếu viết service.
- Dùng **Dependency Injection** thay vì static class.
- Khi làm việc với JSON:
  - Dùng `System.Text.Json` (không dùng `Newtonsoft.Json` trừ khi cần attribute đặc biệt).
- Khi làm việc với thread:
  - Ưu tiên `async/await`.
  - Không block thread bằng `.Result` hoặc `.Wait()`.
- Luôn log exception đầy đủ bằng `ILogger`.

---

## Bình luận & tài liệu
- Thêm **XML comment** (`///`) cho tất cả public class, method, property.
- Viết comment giải thích “tại sao”, không chỉ “cái gì”.
- Khi cần mô tả rõ luồng logic, dùng comment dạng:
  ```csharp
  // Step 1: Validate input
  // Step 2: Authenticate token
  // Step 3: Return result