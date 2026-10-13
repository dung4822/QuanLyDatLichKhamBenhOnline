# API chat với trợ lý AI (tra cứu + đặt/hủy/đổi lịch, sửa hồ sơ)

## Phạm vi

Bệnh nhân chat với trợ lý để tra cứu chuyên khoa, bác sĩ, giờ trống và nhờ **đặt / hủy / đổi lịch, sửa hồ sơ**.

Nguyên tắc quan trọng nhất cho UI:

> Trợ lý **không tự thay đổi dữ liệu**. Khi cần thao tác, response có một **thẻ xác nhận** (`action`).
> Dữ liệu chỉ thay đổi khi người dùng **bấm nút Xác nhận** trên thẻ (gọi API confirm).
> Người dùng gõ "ok", "đồng ý" trong ô chat **không** thay cho việc bấm nút.

- Chỉ tài khoản vai trò `Patient`. Doctor/Admin/Receptionist nhận 403.
- Đăng nhập bằng cookie; mọi `fetch` phải có `credentials: "include"`.
- Mọi POST cần header `X-CSRF-TOKEN` (xem [ACCOUNTS_AUTH_API.md](ACCOUNTS_AUTH_API.md)).
- Hội thoại lưu ở server: reload trang, đổi thiết bị vẫn xem lại được.
- Enum trả về dạng chuỗi (`"Pending"`, `"BookAppointment"`). Timestamp là UTC (`...Z`).
- Lỗi theo ProblemDetails: hiển thị `detail` cho người dùng.

## Endpoint

| Method | Route | Gọi AI? | Kết quả |
|---|---|---|---|
| POST | /api/chats/sessions | Không | Tạo cuộc trò chuyện, 201 + `ChatSession` |
| GET | /api/chats/sessions | Không | 20 cuộc trò chuyện gần nhất (mới nhất trước) |
| GET | /api/chats/sessions/{sessionId} | Không | Cuộc trò chuyện + 50 lượt gần nhất (cũ → mới), kèm thẻ |
| POST | /api/chats/sessions/{sessionId}/messages | **Có** | Gửi tin nhắn, 200 + `ChatResponse` |
| POST | /api/chats/actions/{actionId}/confirm | Không | Người dùng bấm **Xác nhận**, 200 + `ChatAction` |
| POST | /api/chats/actions/{actionId}/reject | Không | Người dùng bấm **Hủy**, 200 + `ChatAction` |

Confirm/reject không cần body. Chỉ endpoint gửi tin nhắn bị giới hạn **10 request/phút/tài khoản** (429).

## Kiểu dữ liệu (TypeScript)

```ts
type ChatSession = {
  chatSessionId: string;      // Guid
  title: string;              // "Cuộc trò chuyện mới", sau tin nhắn đầu = 60 ký tự đầu của tin nhắn đó
  createdAt: string;
  updatedAt: string;
};

type ChatTurn = {
  chatTurnId: number;
  message: string;            // câu người dùng gửi
  reply: string;              // câu trả lời của trợ lý
  createdAt: string;
  action: ChatAction | null;  // thẻ xác nhận tạo trong lượt này (nếu có)
};

type ChatSessionDetail = {
  session: ChatSession;
  turns: ChatTurn[];          // cũ → mới
};

type ChatResponse = {
  chatSessionId: string;
  chatTurnId: number;
  reply: string;
  action: ChatAction | null;
  sources: { tool: string; data: unknown }[]; // dữ liệu tra cứu lượt này, KHÔNG bắt buộc hiển thị
};

type ChatActionType = "BookAppointment" | "CancelAppointment" | "RescheduleAppointment" | "UpdateProfile";
type ChatActionStatus = "Pending" | "Confirmed" | "Rejected" | "Expired" | "Superseded" | "Failed";

type ChatAction = {
  chatActionId: string;       // Guid, dùng cho confirm/reject
  type: ChatActionType;
  status: ChatActionStatus;
  title: string;              // "Đặt lịch khám", "Hủy lịch khám", "Đổi lịch khám", "Cập nhật hồ sơ"
  details: { label: string; before: string | null; value: string }[];
  expiresAt: string;          // UTC; thẻ Pending hết hiệu lực sau 10 phút
  completedAt: string | null;
  failureReason: string | null;
};
```

`details` đã được backend định dạng sẵn bằng tiếng Việt (tên bác sĩ, "Thứ Hai, 13/10/2026", "09:00 - 09:30"...).
UI chỉ cần hiển thị theo thứ tự, không tự ghép hay tra cứu thêm.

## Luồng màn hình

### 1. Mở trang chat

```text
Có sessionId đã lưu (localStorage)?
  ├─ Có → GET /api/chats/sessions/{id}
  │        ├─ 200 → vẽ lại toàn bộ turns và thẻ
  │        └─ 404 → xóa id đã lưu, tạo cuộc trò chuyện mới
  └─ Không → POST /api/chats/sessions → lưu chatSessionId
```

Có thể hiển thị danh sách `GET /api/chats/sessions` để người dùng mở lại cuộc trò chuyện cũ.
Nút "Cuộc trò chuyện mới" = `POST /api/chats/sessions`.

### 2. Gửi tin nhắn

```http
POST /api/chats/sessions/{sessionId}/messages
Content-Type: application/json
X-CSRF-TOKEN: ...
```

```json
{
  "message": "Đặt cho mình bác sĩ An 9h sáng mai",
  "clientMessageId": "6f1c2b8e-5d7a-4c1e-9b0a-2f3e4d5c6b7a"
}
```

- `message`: bắt buộc, không rỗng/toàn khoảng trắng, tối đa 4.000 ký tự. Kiểm tra ở UI trước khi gửi.
- `clientMessageId`: **UI tự tạo** bằng `crypto.randomUUID()` **một lần cho mỗi tin nhắn mới**.
  Không được là `00000000-0000-0000-0000-000000000000`.
- Thời gian xử lý có thể tới **60 giây** (trợ lý tra cứu nhiều bước). Đặt timeout phía client khoảng 75 giây
  và hiển thị trạng thái "Trợ lý đang trả lời…".
- **Khóa ô nhập** trong lúc chờ: không gửi tin nhắn tiếp theo khi tin trước chưa có kết quả.
- Hiển thị ngay tin nhắn của người dùng (optimistic), khi có response thì thêm `reply` và thẻ `action` (nếu có).

### 3. Gửi lại khi mất mạng / timeout — QUAN TRỌNG

Khi request gửi tin nhắn lỗi mạng, timeout, hoặc 502: cho người dùng bấm **"Gửi lại"** và gửi lại
**đúng `message` và đúng `clientMessageId` cũ**. Không tạo Guid mới.

| Tình huống thật ở server | Kết quả khi gửi lại cùng `clientMessageId` |
|---|---|
| Server đã trả lời xong nhưng response bị mất | Trả lại **đúng câu trả lời và thẻ cũ**, không gọi AI lần nữa, không tạo thẻ trùng |
| Server chưa lưu gì (AI lỗi, timeout) | Xử lý như tin nhắn mới |

Tạo Guid mới khi gửi lại có thể khiến trợ lý xử lý yêu cầu hai lần (tốn quota, thẻ mới thay thẻ cũ).
Chỉ tạo Guid mới khi người dùng **gõ tin nhắn mới**.

Nếu người dùng reload trang trong lúc chờ: sau khi tải lại `GET /api/chats/sessions/{id}`, nếu không thấy lượt đó
thì tin nhắn chưa được lưu, người dùng gửi lại bình thường.

### 4. Thẻ xác nhận

Hiển thị thẻ dưới câu trả lời của lượt chứa nó. Theo `status`:

| Status | Hiển thị | Nút |
|---|---|---|
| `Pending` | Tiêu đề + details, đồng hồ đếm ngược tới `expiresAt` | **Xác nhận**, **Hủy** |
| `Confirmed` | "Đã thực hiện" | Không |
| `Rejected` | "Bạn đã hủy đề xuất này" | Không |
| `Expired` | "Đề xuất đã hết hạn, hãy nhờ trợ lý tạo lại" | Không |
| `Superseded` | "Đã được thay bằng đề xuất mới hơn" (có thể làm mờ thẻ) | Không |
| `Failed` | Hiển thị `failureReason` | Không |

- `before` khác `null` (đổi lịch, sửa hồ sơ): hiển thị dạng `before → value`, ví dụ `08:00 - 08:30 → 09:00 - 09:30`.
- Mỗi cuộc trò chuyện chỉ có tối đa **một** thẻ `Pending`. Khi response có thẻ mới, đổi thẻ `Pending` cũ trên màn hình
  thành `Superseded` (hoặc tải lại cuộc trò chuyện).
- Khi đồng hồ về 0: tự chuyển thẻ sang hiển thị `Expired` ở UI (server cũng sẽ trả `Expired` khi tải lại).
  Không dựa vào giờ máy người dùng để cho phép xác nhận; nếu bấm muộn, server trả 409.

### 5. Bấm Xác nhận / Hủy

```http
POST /api/chats/actions/{chatActionId}/confirm
X-CSRF-TOKEN: ...
```

- Khóa nút trong lúc gọi. Bấm hai lần vẫn an toàn (chỉ một lịch được tạo), nhưng nên khóa để tránh nhầm lẫn.
- **200**: thay thẻ bằng `ChatAction` trả về (`Confirmed` hoặc `Rejected`).
  Sau khi xác nhận đặt/đổi/hủy lịch, tải lại danh sách lịch hẹn (`GET /api/me/appointments`) nếu đang hiển thị;
  sau khi sửa hồ sơ, tải lại `GET /api/me/profile`.
- **Không cần** gửi tin nhắn báo cho trợ lý; ở lượt chat sau, trợ lý tự biết thẻ đã được xác nhận.
- Mất response khi confirm: gọi lại confirm (thẻ đã `Confirmed` thì vẫn trả 200 với kết quả cũ),
  hoặc tải lại cuộc trò chuyện để xem trạng thái.

Ví dụ response confirm:

```json
{
  "chatActionId": "0b6a4f0e-2a3b-4c5d-8e9f-1a2b3c4d5e6f",
  "type": "BookAppointment",
  "status": "Confirmed",
  "title": "Đặt lịch khám",
  "details": [
    { "label": "Bác sĩ", "before": null, "value": "Nguyễn Văn An" },
    { "label": "Chuyên khoa", "before": null, "value": "Nội tổng quát" },
    { "label": "Ngày khám", "before": null, "value": "Thứ Hai, 13/10/2026" },
    { "label": "Giờ khám", "before": null, "value": "09:00 - 09:30" },
    { "label": "Lý do khám", "before": null, "value": "Đau đầu" }
  ],
  "expiresAt": "2026-10-07T03:10:00Z",
  "completedAt": "2026-10-07T03:02:41Z",
  "failureReason": null
}
```

## Mã lỗi và cách xử lý

| Mã | Khi nào | UI nên làm |
|---|---|---|
| 400 | `message` rỗng/quá 4.000 ký tự, `clientMessageId` toàn số 0, hoặc thiếu/sai CSRF | Lỗi CSRF: lấy token mới rồi gửi lại. Lỗi dữ liệu: hiện `detail`/`errors` |
| 401 | Chưa đăng nhập / phiên hết hạn | Chuyển tới đăng nhập |
| 403 | Không phải tài khoản bệnh nhân | Ẩn chức năng chat |
| 404 | Cuộc trò chuyện/thẻ không tồn tại hoặc của người khác | Cuộc trò chuyện: tạo mới. Thẻ: tải lại cuộc trò chuyện |
| 409 | Confirm/reject thẻ không còn `Pending` (hết hạn, đã hủy, đã thay thế); hoặc nghiệp vụ từ chối khi xác nhận (slot vừa bị đặt, quá hạn đổi lịch, hồ sơ đã bị sửa ở nơi khác...) | Hiện `detail`, rồi **tải lại cuộc trò chuyện** để vẽ đúng trạng thái thẻ (thường là `Failed` kèm lý do) |
| 429 | Gửi quá 10 tin nhắn/phút | Hiện "Bạn gửi quá nhanh", cho gửi lại sau ít phút (giữ nguyên `clientMessageId`) |
| 502 | Dịch vụ AI lỗi, hết thời gian chờ, chưa cấu hình | Hiện `detail`, nút "Gửi lại" với **cùng** `clientMessageId`. Không có gì được lưu |

## Hiển thị nội dung an toàn

- `reply` là văn bản do AI viết. Hiển thị như **văn bản thuần** (giữ xuống dòng), hoặc markdown đã được sanitize.
  **Không** dùng `innerHTML` / `dangerouslySetInnerHTML` với nội dung thô.
- Không suy ra trạng thái thao tác từ lời văn của trợ lý. Chỉ tin `action.status` từ API.
- `sources` là dữ liệu kỹ thuật cho debug; không bắt buộc hiển thị, không dùng ID trong đó để tự gọi API ghi dữ liệu.

## Ví dụ code (fetch)

```js
const apiUrl = "https://localhost:7023";
let csrfToken; // lấy từ GET /api/auth/csrf, xem ACCOUNTS_AUTH_API.md

async function api(path, options = {}) {
  const response = await fetch(`${apiUrl}${path}`, {
    ...options,
    credentials: "include",
    headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrfToken, ...options.headers }
  });
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) throw { status: response.status, problem: body };
  return body;
}

// Mỗi tin nhắn mới tạo một Guid; giữ Guid này để "Gửi lại" khi lỗi.
function newPendingMessage(text) {
  return { message: text.trim(), clientMessageId: crypto.randomUUID() };
}

async function sendMessage(sessionId, pending) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 75_000);
  try {
    return await api(`/api/chats/sessions/${sessionId}/messages`, {
      method: "POST",
      body: JSON.stringify(pending), // lần gửi lại dùng lại đúng object này
      signal: controller.signal
    });
  } finally {
    clearTimeout(timer);
  }
}

async function confirmAction(sessionId, actionId) {
  try {
    return await api(`/api/chats/actions/${actionId}/confirm`, { method: "POST" });
  } catch (error) {
    if (error.status === 409 || error.status === 404) {
      showError(error.problem?.detail);
      return reloadSession(sessionId); // GET /api/chats/sessions/{id} rồi vẽ lại thẻ
    }
    throw error;
  }
}
```

## Ví dụ một cuộc trò chuyện

1. "Bác sĩ chuyên khoa tim mạch ngày mai còn giờ nào?" → `reply` liệt kê bác sĩ, giờ trống; `action: null`.
2. "Đặt bác sĩ An lúc 9h, mình bị đau ngực" → `reply` nhắc kiểm tra thẻ; `action` = thẻ `BookAppointment` `Pending`.
3. Người dùng bấm **Xác nhận** → thẻ `Confirmed`, lịch hẹn đã được tạo.
4. "Đổi sang 10h được không?" → thẻ `RescheduleAppointment` với `before` = giờ cũ.
5. "Hủy lịch đó giúp mình" → thẻ `CancelAppointment`.
6. "Đổi số điện thoại của mình thành 0912345678" → thẻ `UpdateProfile`, details chỉ gồm trường thay đổi.

Trợ lý không chẩn đoán, kê đơn hay tư vấn điều trị. Giờ khám theo giờ Việt Nam, chỉ đặt trong 7 ngày từ hôm nay.
Chi tiết nghiệp vụ backend: [DEEPSEEK_CHAT.md](../learning/DEEPSEEK_CHAT.md).
