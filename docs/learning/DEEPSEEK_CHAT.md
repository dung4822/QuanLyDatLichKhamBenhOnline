# Chat DeepSeek — mức 1 và 2

Chat có ngữ cảnh ngắn và tra cứu dữ liệu thật: chuyên khoa hoạt động, bác sĩ và giờ trống.
Giai đoạn này dùng backend/Swagger. Form và nút xác nhận đặt lịch thuộc bước tiếp theo.

## Cấu hình

`WebDatLichKhamBenh.Api/appsettings.json` có sẵn:

```json
"DeepSeek": {
  "ApiKey": "",
  "Model": "deepseek-flash",
  "MaxOutputTokens": 1024
}
```

Chỉ cần thêm key. Khuyến nghị mở **Manage User Secrets** của project API và thêm
`"DeepSeek": { "ApiKey": "KEY_CUA_BAN" }` vào JSON đang có, giữ nguyên cấu hình khác.
Hoặc chạy tại thư mục solution:

```powershell
dotnet user-secrets set "DeepSeek:ApiKey" "KEY_CUA_BAN" --project WebDatLichKhamBenh.Api
```

User Secrets được tự đọc trong Development. Khi triển khai dùng biến môi trường `DeepSeek__ApiKey`
hoặc secret của môi trường chạy. Không commit key vào Git. Không cần Gemini key.
Khởi động lại API sau khi đổi cấu hình.

Model mặc định được chọn ngày 03/10/2026: `deepseek-flash` (V4.1 Flash), có tool calls và giá thấp hơn Pro.
Client chủ động tắt thinking cho luồng tra cứu đơn giản. Dùng `HttpClient`, không cần SDK mới.

- Model/giá: https://api-docs.deepseek.com/quick_start/pricing/
- Tool calls: https://api-docs.deepseek.com/guides/tool_calls/
- Thinking: https://api-docs.deepseek.com/guides/thinking_mode/

## Thử bằng Swagger

```powershell
dotnet run --project WebDatLichKhamBenh.Api --launch-profile https
```

1. Mở `https://localhost:7023/swagger`.
2. Gọi `GET /api/auth/csrf`, dán `requestToken` vào **Authorize → CSRF**.
3. Thử nhanh bằng `POST /api/chats/test`: không cần đăng nhập, vẫn cần CSRF; chỉ có trong Development.
4. Luồng chính `POST /api/chats` yêu cầu vai trò `Patient`. Đăng nhập qua `/api/auth/login`,
   sau đó lấy CSRF token mới trước khi gửi chat.

```json
{
  "message": "Phòng khám hiện có những chuyên khoa nào?",
  "history": []
}
```

Response gồm:

- `reply`: câu trả lời do AI diễn đạt.
- `sources`: kết quả backend tra trong lượt hiện tại, mỗi phần có `tool` và `data`.
  UI sau này lấy ID và các lựa chọn từ đây, không trích chúng từ lời văn của AI.
- `history`: các cặp câu hỏi/trả lời gần nhất đã giới hạn độ dài.

Để hỏi tiếp, gửi `message` mới và sao chép nguyên `history` từ response trước.
History chỉ gồm các cặp `{ "role": "user", "content": "..." }`,
`{ "role": "assistant", "content": "..." }`. Backend chưa lưu hội thoại vào database.
History do client gửi chỉ là ngữ cảnh, không có quyền `system`/`tool` và không phải bằng chứng dữ liệu hiện tại.

Câu hỏi nên thử với tên có thật trong database:

- “Có chuyên khoa [tên chuyên khoa] không?”
- “Tìm bác sĩ chuyên khoa [tên chuyên khoa].”
- “Bác sĩ [tên từ kết quả] ngày mai còn giờ nào?”
- “Đặt lịch giúp mình.” → có thể tra giờ và hướng dẫn dùng chức năng đặt lịch hiện tại; chưa tạo lịch qua chat.

Không có dữ liệu/slot thì kết quả rỗng. Slot theo quy tắc hiện tại: hôm nay đến 6 ngày tiếp theo theo giờ Việt Nam,
chưa đến giờ bắt đầu. Background job hiện có phụ trách tạo/cập nhật slot.

## Luồng xử lý

```text
ChatsController → ChatService → ILlmClient → DeepSeekClient
                     ↓ khi model yêu cầu công cụ
              IChatLookupService
                     ↓
           Service → Repository → PostgreSQL
                     ↓
         kết quả gửi lại DeepSeek để diễn đạt
                     ↓
            reply + sources + history
```

| Công cụ | Tham số |
|---|---|
| `get_specialties` | `query` tùy chọn |
| `get_doctors` | `specialtyId`, `query` tùy chọn |
| `get_available_slots` | `doctorId` bắt buộc; `fromDate`, `toDate` tùy chọn, định dạng `yyyy-MM-dd` |

Chỉ ba công cụ trên được thực thi. Tìm tên không phân biệt dấu tiếng Việt. Backend chỉ trả các trường cần thiết,
không gửi email, điện thoại, địa chỉ bác sĩ hoặc hồ sơ bệnh nhân cho LLM. Chat không có công cụ SQL hay tạo/đổi/hủy lịch.

## Giới hạn và kiểm thử

- Message tối đa 4.000 ký tự; history tối đa 5 cặp, mỗi nội dung tối đa 4.000 ký tự; tổng tối đa 16.000 ký tự.
- Response giữ history tối đa 12.000 ký tự để chừa chỗ cho câu hỏi tiếp theo.
- Mỗi lượt tối đa 4 lần gọi LLM, 6 lần tra công cụ và 60 giây. Mỗi truy vấn trả tối đa 20 kết quả cùng `hasMore`.
- Tối đa 10 request chat/phút/tài khoản; request thử chưa đăng nhập tính theo IP.
- `MaxOutputTokens` mặc định 1024, chặn trong khoảng 128–2048. Phản hồi bị cắt được báo lỗi.
- HTTP 400: dữ liệu/CSRF sai; 401/403: đăng nhập/quyền; 429: giới hạn ứng dụng;
  502: thiếu key, số dư, provider, mạng hoặc timeout. Không trả lỗi thô của provider.

```powershell
dotnet test tests/WebDatLichKhamBenh.IntegrationTests --filter "FullyQualifiedName~ChatApiTests|FullyQualifiedName~DeepSeekClientTests|FullyQualifiedName~ChatLookupServiceTests"
```

Ba nhóm test trên dùng provider và dữ liệu giả lập, không tiêu quota, không cần PostgreSQL.
CSRF vẫn được kiểm thật. Data Protection dùng khóa tạm trong test để không phụ thuộc key Windows.
Thử thật qua Swagger cần DeepSeek key, kết nối mạng và PostgreSQL của ứng dụng.

Đã kiểm tra ngày 04/10/2026: build thành công, 85 test liên quan đạt; gọi DeepSeek thật và đi qua ba lượt
tra chuyên khoa → hỏi tiếp bác sĩ theo ngữ cảnh → tra giờ trống từ PostgreSQL thành công.
