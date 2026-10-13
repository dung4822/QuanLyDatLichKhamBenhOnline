# Chat DeepSeek — tra cứu + đặt/hủy/đổi lịch, sửa hồ sơ qua chat

Bệnh nhân chat với trợ lý AI để tra cứu chuyên khoa, bác sĩ, giờ trống, và nhờ **đặt / hủy / đổi lịch, sửa hồ sơ**.
AI **không tự ghi dữ liệu**: AI chỉ tạo **thẻ xác nhận**, dữ liệu chỉ thay đổi khi người dùng tự bấm **Xác nhận**.

> Người làm frontend: đọc [docs/api/CHATS_API.md](../api/CHATS_API.md) (kiểu dữ liệu, luồng màn hình, gửi lại, mã lỗi).

## Cấu hình

`WebDatLichKhamBenh.Api/appsettings.json`:

```json
"DeepSeek": {
  "ApiKey": "",
  "Model": "deepseek-flash",
  "MaxOutputTokens": 1024
}
```

Khuyến nghị để key trong User Secrets, không commit key vào Git:

```powershell
dotnet user-secrets set "DeepSeek:ApiKey" "KEY_CUA_BAN" --project WebDatLichKhamBenh.Api
```

Khi triển khai dùng biến môi trường `DeepSeek__ApiKey`. Khởi động lại API sau khi đổi cấu hình.

Tính năng này thêm 3 bảng mới (`ChatSessions`, `ChatTurns`, `ChatActions`). Cập nhật database trước khi chạy:

```powershell
dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api
```

## Ý tưởng chính: "Đề xuất → Xác nhận"

```text
User: "Đặt cho mình BS An 9h thứ Hai, lý do đau đầu"
  │
  ▼
AI tra cứu: get_doctors → get_available_slots → propose_book_appointment(slotId, reason)
  │                                                  ↑ AI chỉ được ĐỀ XUẤT
  ▼
Backend kiểm tra sơ bộ (slot còn trống, lịch đúng chủ...) → lưu ChatAction (Pending, hết hạn sau 10 phút)
  │
  ▼
Response có "action": thẻ xác nhận, dữ liệu lấy từ DB (không phải chữ do AI viết)
  ┌──────────────────────────────────────┐
  │ Đặt lịch khám                         │
  │ Bác sĩ: Nguyễn Văn An                 │
  │ Ngày khám: Thứ Hai, 13/10/2026        │
  │ Giờ khám: 09:00 - 09:30               │
  │        [Xác nhận]     [Hủy]           │
  └──────────────────────────────────────┘
  │  người dùng bấm nút (KHÔNG đi qua AI)
  ▼
POST /api/chats/actions/{id}/confirm → AppointmentService.CreateAsync() (service sẵn có)
```

Vì sao làm vậy?

| Vấn đề | Cách giải quyết |
|---|---|
| AI hiểu nhầm, bịa ID, bị prompt injection | AI không có công cụ ghi. Chỉ người dùng bấm nút mới ghi dữ liệu. |
| AI "quên" ID slot ở lượt trước | ID nằm trong `ChatActions.PayloadJson` trong DB, không phụ thuộc trí nhớ AI. |
| Mất mạng giữa chừng, gửi lại tin nhắn | `clientMessageId` giống nhau → trả lại câu trả lời cũ, không gọi AI lại, không tạo thẻ trùng. |
| Bấm Xác nhận 2 lần / mất response rồi bấm lại | Confirm idempotent: lần sau trả lại kết quả cũ, chỉ có 1 lịch hẹn được tạo. |
| Reload trang | Hội thoại và trạng thái thẻ lưu ở server: `GET /api/chats/sessions/{id}`. |
| Slot bị người khác đặt trong lúc chờ bấm | Lúc confirm, `AppointmentService` kiểm tra lại đầy đủ → 409, thẻ chuyển `Failed` kèm lý do. |
| Hồ sơ bị sửa ở nơi khác sau khi tạo thẻ | Thẻ sửa hồ sơ lưu "phiên bản" hồ sơ → không ghi đè, trả 409. |
| Bệnh nhân A thao tác lịch của B | `PatientId` luôn lấy từ cookie đăng nhập, AI không bao giờ truyền được. Lịch của người khác báo "không tồn tại". |

## API

Tất cả yêu cầu đăng nhập vai trò `Patient` và header CSRF cho POST.

| Method | Route | Mô tả |
|---|---|---|
| POST | `/api/chats/sessions` | Tạo cuộc trò chuyện mới → `201` + `ChatSessionDto` |
| GET | `/api/chats/sessions` | 20 cuộc trò chuyện gần nhất |
| GET | `/api/chats/sessions/{sessionId}` | 50 lượt gần nhất, mỗi lượt kèm thẻ (nếu có). Dùng khi reload. |
| POST | `/api/chats/sessions/{sessionId}/messages` | Gửi tin nhắn (gọi AI, giới hạn 10 lần/phút) |
| POST | `/api/chats/actions/{actionId}/confirm` | Người dùng bấm **Xác nhận** |
| POST | `/api/chats/actions/{actionId}/reject` | Người dùng bấm **Hủy** |

### Gửi tin nhắn

```json
{
  "message": "Đặt cho mình bác sĩ An 9h sáng mai",
  "clientMessageId": "6f1c2b8e-5d7a-4c1e-9b0a-2f3e4d5c6b7a"
}
```

`clientMessageId`: client tạo `crypto.randomUUID()` cho **mỗi tin nhắn mới**, và **dùng lại đúng giá trị đó**
khi gửi lại tin nhắn vì lỗi mạng/timeout. Bỏ trống thì server tự tạo (khi đó gửi lại = tin nhắn mới).

Response:

```json
{
  "chatSessionId": "…",
  "chatTurnId": 12,
  "reply": "Mình đã tạo thẻ đặt lịch, bạn kiểm tra rồi bấm Xác nhận nhé.",
  "action": {
    "chatActionId": "…",
    "type": "BookAppointment",
    "status": "Pending",
    "title": "Đặt lịch khám",
    "details": [
      { "label": "Bác sĩ", "before": null, "value": "Nguyễn Văn An" },
      { "label": "Giờ khám", "before": null, "value": "09:00 - 09:30" }
    ],
    "expiresAt": "2026-10-07T03:10:00Z",
    "completedAt": null,
    "failureReason": null
  },
  "sources": [ { "tool": "get_available_slots", "data": { } } ]
}
```

- `action` = `null` khi lượt này không có đề xuất.
- `details[].before` chỉ có khi đổi lịch / sửa hồ sơ (hiển thị "cũ → mới").
- `sources` chỉ có ở lượt vừa gửi, không lưu lại.

### Trạng thái thẻ (`status`)

| Status | Ý nghĩa | UI |
|---|---|---|
| `Pending` | Chờ người dùng quyết định | Hiện 2 nút Xác nhận / Hủy |
| `Confirmed` | Đã thực hiện thành công | "Đã xong" |
| `Rejected` | Người dùng bấm Hủy | Ẩn nút |
| `Expired` | Quá 10 phút chưa bấm | Ẩn nút, gợi ý nhờ trợ lý tạo lại |
| `Superseded` | Có thẻ mới hơn thay thế | Ẩn nút |
| `Failed` | Đã bấm Xác nhận nhưng nghiệp vụ từ chối | Hiện `failureReason` |

Mỗi cuộc trò chuyện chỉ có **một** thẻ `Pending`; thẻ mới tự thay thẻ cũ.

### Mã lỗi khi Xác nhận / Hủy

- `200`: thành công (bấm lại thẻ đã `Confirmed` vẫn trả `200` với kết quả cũ).
- `404`: thẻ không tồn tại hoặc thuộc người khác.
- `409`: thẻ không còn hiệu lực (hết hạn, đã hủy, đã thay thế) hoặc nghiệp vụ từ chối (slot vừa bị đặt…).
  Sau `409`, gọi lại `GET /api/chats/sessions/{id}` để vẽ lại thẻ.

## Công cụ của AI

| Công cụ | Loại | Tham số |
|---|---|---|
| `get_specialties` | đọc | `query` tùy chọn |
| `get_doctors` | đọc | `specialtyId`, `query` tùy chọn |
| `get_available_slots` | đọc | `doctorId` bắt buộc; `fromDate`, `toDate` (`yyyy-MM-dd`) |
| `get_my_appointments` | đọc | không có — chỉ lịch sắp tới của người đang chat |
| `propose_book_appointment` | đề xuất | `appointmentSlotId`, `reason` |
| `propose_cancel_appointment` | đề xuất | `appointmentId`, `reason` |
| `propose_reschedule_appointment` | đề xuất | `appointmentId`, `newAppointmentSlotId` |
| `propose_update_profile` | đề xuất | chỉ các trường muốn đổi: `fullName`, `dateOfBirth`, `gender` (0 = Nam, 1 = Nữ), `phoneNumber`, `email`, `address` |

Không gửi cho AI: số điện thoại/email/địa chỉ bác sĩ, lý do khám của lịch cũ, giá trị hồ sơ cũ
(giá trị cũ chỉ hiện trên thẻ cho người dùng).

## Trí nhớ và chi phí

- **Lưu toàn bộ** hội thoại trong DB (rẻ), nhưng **chỉ gửi cho AI 5 lượt gần nhất** (tối đa 12.000 ký tự).
  Chi phí mỗi câu hỏi không tăng theo độ dài cuộc trò chuyện.
- Không tóm tắt hội thoại: thông tin quan trọng (ID slot, lịch hẹn) đã nằm trong bảng `ChatActions`.
- Mỗi lượt, AI được báo **trạng thái thật** của các thẻ gần đây (Pending/Confirmed/Failed…), để không nói sai
  "đã đặt" khi người dùng chưa bấm, và biết khi người dùng đã xác nhận.
- Mỗi câu hỏi tối đa 6 lần gọi AI, 8 lần dùng công cụ, 60 giây. Bấm Xác nhận/Hủy không gọi AI, không tốn token.

## Thử bằng Swagger

```powershell
dotnet run --project WebDatLichKhamBenh.Api --launch-profile https
```

1. Mở `https://localhost:7023/swagger`, gọi `GET /api/auth/csrf`, dán `requestToken` vào **Authorize → CSRF**.
2. Đăng nhập tài khoản bệnh nhân qua `POST /api/auth/login`, rồi lấy CSRF token mới.
3. `POST /api/chats/sessions` → lấy `chatSessionId`.
4. `POST /api/chats/sessions/{id}/messages` với `{ "message": "..." }`.
   Trong Swagger nên **xóa dòng `clientMessageId`** (Swagger điền sẵn cùng một Guid mẫu, gửi lại sẽ nhận câu trả lời cũ).
5. Khi response có `action`, gọi `POST /api/chats/actions/{chatActionId}/confirm` (hoặc `/reject`).

Câu nên thử: "Tìm bác sĩ chuyên khoa [tên]" → "Bác sĩ [tên] ngày mai còn giờ nào?" → "Đặt giờ 9h nhé" → bấm confirm
→ "Mình muốn đổi sang 10h" → "Hủy lịch đó giúp mình" → "Đổi số điện thoại của mình thành 09xxxxxxxx".

Endpoint thử nghiệm không cần đăng nhập `POST /api/chats/test` đã bỏ: chat giờ cần biết bệnh nhân là ai để lưu hội thoại
và thao tác lịch.

## Luồng code

```text
ChatsController
  ├─ ChatService            → lưu/đọc hội thoại (IChatRepository), gọi AI (ILlmClient → DeepSeekClient)
  │     ├─ ChatLookupService  → công cụ tra cứu công khai
  │     └─ ChatActionService  → get_my_appointments + propose_* (tạo ChatAction, CHƯA lưu)
  │   ChatService lưu ChatTurn + ChatAction trong cùng một lần SaveChanges
  └─ ChatActionService.ConfirmAsync / RejectAsync
        → AppointmentService / AccountService (service sẵn có, tự kiểm tra quyền + quy tắc lịch)
```

## Kiểm thử

```powershell
# Không cần PostgreSQL (AI, kho chat, công cụ đều giả lập):
dotnet test tests/WebDatLichKhamBenh.IntegrationTests --filter "FullyQualifiedName~ChatApiTests|FullyQualifiedName~DeepSeekClientTests|FullyQualifiedName~ChatLookupServiceTests"

# Luồng xác nhận với PostgreSQL thật (chỉ AI giả lập):
dotnet test tests/WebDatLichKhamBenh.IntegrationTests --filter "FullyQualifiedName~ChatActionApiTests"
```

Test cần PostgreSQL đọc biến môi trường `APPOINTMENT_TEST_POSTGRES` (mặc định `127.0.0.1:55439`); test tự tạo
database tạm `appointment_tests_*` rồi xóa đi. `ChatActionApiTests` kiểm: chưa bấm thì chưa có lịch, bấm 2 lần cùng lúc
chỉ tạo 1 lịch, slot bị chiếm thì thẻ `Failed`, thẻ hết hạn/đã hủy không xác nhận được, người khác không xác nhận được,
đổi/hủy lịch, không đụng được lịch người khác, sửa hồ sơ không ghi đè thay đổi mới hơn, thẻ mới thay thẻ cũ,
gửi lại cùng `clientMessageId` không gọi AI lần nữa.

Xem [hướng dẫn chạy test local](../../tests/README.md) để cấu hình PostgreSQL và chạy toàn bộ bộ test.

Đã kiểm tra ngày 13/10/2026: build thành công, 172/172 test đạt (PostgreSQL local).
