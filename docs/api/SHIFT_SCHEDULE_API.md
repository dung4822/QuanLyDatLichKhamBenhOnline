# SHIFT VÀ LỊCH TUẦN BÁC SĨ — FRONTEND HANDOFF

## Mục tiêu và API dùng cho màn hình

Màn hình **Cấu hình lịch làm việc tuần của bác sĩ** cho phép admin chọn một bác sĩ, tick nhiều ca làm việc trong tuần, sau đó bấm **Lưu** đúng một lần.

```text
API base URL: https://localhost:7023
Fallback HTTP: http://localhost:5244
```

Màn hình này dùng ba API sau:

```http
GET /api/shifts
GET /api/doctors/{doctorId}/shifts
PUT /api/doctors/{doctorId}/shifts
```

> Không gọi một API tạo `DoctorShift` cho từng checkbox. API `PUT` đã là API chính cho cả tạo mới lẫn sửa lịch tuần.

## Mô hình dữ liệu

- `Shift` là một ca làm việc dùng chung: thứ trong tuần, giờ bắt đầu, giờ kết thúc, tên ca và trạng thái hoạt động.
- `DoctorShift` chỉ là liên kết: **bác sĩ nào làm các Shift nào mỗi tuần**.
- `DoctorShift` không chứa một ngày cụ thể, không phải ca thực tế hay `AppointmentSlot`.
- Khi có `NonWorkingDay` và `AppointmentSlot` sau này, backend sẽ dùng lịch tuần này làm đầu vào để sinh slot theo ngày thực tế.

Ví dụ: `ShiftId = 3` là thứ Hai, 08:00–11:00. Nếu Doctor 5 chọn Shift 3, nghĩa là bác sĩ 5 thường làm ca đó vào mỗi thứ Hai.

## Kiểu dữ liệu ở frontend

API JSON dùng `camelCase`. Vì backend cấu hình enum dạng chuỗi, `dayOfWeek` là chuỗi tiếng Anh đúng tên enum, không phải số.

```ts
type DayOfWeek =
  | "Sunday"
  | "Monday"
  | "Tuesday"
  | "Wednesday"
  | "Thursday"
  | "Friday"
  | "Saturday";

interface Shift {
  shiftId: number;
  dayOfWeek: DayOfWeek;
  name: string;
  startTime: string; // "HH:mm:ss", ví dụ "08:00:00"
  endTime: string;   // "HH:mm:ss", ví dụ "11:00:00"
  isActive: boolean;
  createdAt: string; // ISO UTC datetime
  updatedAt: string | null;
}

interface DoctorShift {
  doctorShiftId: number;
  doctorId: number;
  doctorFullName: string;
  shiftId: number;
  shiftName: string;
  dayOfWeek: DayOfWeek;
  startTime: string;
  endTime: string;
  createdAt: string;
  updatedAt: string | null;
}

interface ReplaceDoctorShiftsRequest {
  shiftIds: number[];
}
```

## Bảng Shift: API và ràng buộc dữ liệu

### Lấy danh sách ca

```http
GET /api/shifts
```

- **200 OK:** trả `Shift[]`, đã sắp xếp thứ Hai → Chủ Nhật, sau đó theo `startTime`.
- Danh sách bao gồm cả ca `isActive: false`.
- Danh sách không bao gồm ca đã xóa mềm.

UI nên hiển thị ca inactive với badge **Ngừng hoạt động** và disable checkbox của ca đó trong màn hình lịch bác sĩ.

### Tạo ca

```http
POST /api/shifts
Content-Type: application/json
```

```json
{
  "dayOfWeek": "Monday",
  "name": "Ca sáng",
  "startTime": "08:00:00",
  "endTime": "11:00:00",
  "isActive": true
}
```

- **201 Created:** trả một `Shift`.
- Khi tạo mới, có thể không gửi `isActive`; backend mặc định là `true`. UI vẫn nên luôn gửi rõ `true` hoặc `false` để request dễ đọc và nhất quán.

### Sửa ca

```http
PUT /api/shifts/{shiftId}
Content-Type: application/json
```

```json
{
  "dayOfWeek": "Monday",
  "name": "Ca sáng",
  "startTime": "08:00:00",
  "endTime": "11:30:00",
  "isActive": true
}
```

- **200 OK:** trả `Shift` sau khi sửa.
- **404 Not Found:** ca không tồn tại hoặc đã xóa mềm.
- Đây là request thay toàn bộ dữ liệu của ca. UI phải luôn gửi đủ 5 field ở trên, đặc biệt phải gửi `isActive`; không gửi field này có thể bị hiểu là `false`.

### Xóa ca

```http
DELETE /api/shifts/{shiftId}
```

- **204 No Content:** thành công.
- Đây là xóa mềm. Ca sẽ không còn trong `GET /api/shifts` và không thể được phân công trong lịch bác sĩ.
- UI cần xác nhận trước khi xóa. Nếu ca đang được dùng cho lịch bác sĩ, nên ưu tiên chuyển `isActive` thành `false` thay vì xóa.

### Ràng buộc backend là nguồn sự thật

| Field | UI phải gửi | Backend đang kiểm tra |
| --- | --- | --- |
| `dayOfWeek` | Một trong 7 chuỗi enum ở trên | Bắt buộc hợp lệ trong enum. |
| `name` | Chuỗi đã `trim()` | Không rỗng/chỉ khoảng trắng; tối đa 100 ký tự. Backend cũng trim trước khi lưu. |
| `startTime` | `HH:mm:ss` | Có kiểu thời gian hợp lệ. |
| `endTime` | `HH:mm:ss` | Phải **sau** `startTime`. Không hỗ trợ ca qua đêm, ví dụ 22:00–02:00. |
| `isActive` | Boolean | Tạo mới mặc định `true`; khi sửa UI phải gửi rõ giá trị. |

Ràng buộc cần áp dụng ở UI để người dùng nhận phản hồi sớm:

- Dùng input `time`; trước khi gửi nối thêm `:00` nếu trình duyệt trả về `HH:mm`.
- Không cho lưu khi `endTime <= startTime`.
- Trim `name`, báo lỗi khi trống hoặc dài quá 100 ký tự.
- Trong mỗi ngày, UI **nên** cảnh báo khi các khoảng giờ bị chồng lên nhau. Backend hiện chưa có ràng buộc chống overlap hoặc chống trùng `name`/giờ, nên đây là kiểm tra hỗ trợ UX, không được coi là đã được backend bảo đảm.

## Lịch tuần bác sĩ: contract cho giao diện checkbox

### 1. Khi admin chọn một bác sĩ

Gọi hai API:

```http
GET /api/shifts
GET /api/doctors/{doctorId}/shifts
```

Ví dụ response của API lịch tuần:

```json
[
  {
    "doctorShiftId": 12,
    "doctorId": 5,
    "doctorFullName": "Nguyễn Văn An",
    "shiftId": 3,
    "shiftName": "Ca sáng",
    "dayOfWeek": "Monday",
    "startTime": "08:00:00",
    "endTime": "11:00:00",
    "createdAt": "2026-08-09T08:30:00Z",
    "updatedAt": null
  }
]
```

- **200 OK + `[]`:** bác sĩ tồn tại nhưng chưa có ca nào trong lịch tuần.
- **404 Not Found:** bác sĩ không tồn tại hoặc đã xóa mềm.

Tạo tập ID đã chọn từ response lịch tuần:

```ts
const selectedShiftIds = new Set(doctorShifts.map((item) => item.shiftId));

const isChecked = (shift: Shift) => selectedShiftIds.has(shift.shiftId);
const isDisabled = (shift: Shift) => !shift.isActive;
```

### 2. Hiển thị checkbox

Nhóm `Shift` theo `dayOfWeek`, hiển thị theo thứ tự sau để phù hợp lịch làm việc ở Việt Nam:

```ts
const weekOrder: DayOfWeek[] = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday"
];
```

Mỗi checkbox nên hiển thị ví dụ: `Ca sáng — 08:00–11:00`.

- Ca có `isActive: false`: checkbox disabled, không cho tick mới.
- Nếu ca inactive từng được phân công từ trước, UI không được tự gửi lại `shiftId` đó khi bấm Lưu; backend sẽ từ chối ca inactive.
- Có thể thêm nút **Chọn tất cả ca đang hoạt động** và **Bỏ chọn tất cả**. Nút bỏ chọn gửi `shiftIds: []` khi lưu.

### 3. Lưu toàn bộ selection một lần

```http
PUT /api/doctors/5/shifts
Content-Type: application/json
```

```json
{
  "shiftIds": [1, 3, 4, 6, 8, 10]
}
```

Quy tắc rất quan trọng: `shiftIds` là **toàn bộ trạng thái checkbox sau khi admin chỉnh sửa**, không phải chỉ các checkbox vừa tick thêm.

Ví dụ bác sĩ đang có `[1, 3, 4]`, admin bỏ `3` và tick `6`. UI phải gửi:

```json
{ "shiftIds": [1, 4, 6] }
```

Backend sẽ giữ ca `1`, `4`; xóa mềm liên kết của ca `3`; tạo liên kết mới cho ca `6`.

- **200 OK:** trả lại `DoctorShift[]` chính là lịch tuần hiện tại sau khi lưu. Dùng response này để cập nhật `selectedShiftIds` của UI.
- **404 Not Found:** bác sĩ không tồn tại/đã xóa mềm.
- **400 Bad Request:** dữ liệu không hợp lệ.
- **500 Internal Server Error:** không lưu được database; giữ nguyên checkbox trên UI để admin thử lại.

Request này có tính idempotent: gửi lại đúng cùng một danh sách sẽ không tạo thêm dữ liệu trùng.

### Validation của API lịch tuần

| Tình huống | Kết quả |
| --- | --- |
| `shiftIds` là `null` | 400. |
| Một ID nhỏ hơn hoặc bằng 0 | 400. |
| Một ID xuất hiện hai lần | 400. |
| Một Shift không tồn tại hoặc đã xóa mềm | 400. |
| Một Shift có `isActive: false` | 400. |
| `shiftIds: []` | Hợp lệ: gỡ toàn bộ ca khỏi lịch tuần của bác sĩ. |

Response lỗi validation theo chuẩn ASP.NET Core, thường có dạng:

```json
{
  "errors": {
    "ShiftIds": [
      "Không thể phân công các ca đang ngừng hoạt động: 8."
    ]
  }
}
```

Frontend cần hiển thị `errors.ShiftIds` ở phía trên nhóm checkbox hoặc dưới nút Lưu.

## Pseudocode gọi API

```ts
async function loadDoctorWeeklySchedule(doctorId: number) {
  const [shifts, doctorShifts] = await Promise.all([
    api.get<Shift[]>("/api/shifts"),
    api.get<DoctorShift[]>(`/api/doctors/${doctorId}/shifts`)
  ]);

  return {
    shifts: shifts.data,
    selectedShiftIds: new Set(doctorShifts.data.map((item) => item.shiftId))
  };
}

async function saveDoctorWeeklySchedule(doctorId: number, selectedShiftIds: Set<number>) {
  const response = await api.put<DoctorShift[]>(`/api/doctors/${doctorId}/shifts`, {
    shiftIds: [...selectedShiftIds]
  });

  return new Set(response.data.map((item) => item.shiftId));
}
```

Khi request đang chạy, disable nút Lưu và không cho đổi bác sĩ. Sau khi request lỗi, giữ nguyên các checkbox để admin sửa và gửi lại.

## Những API/chức năng chưa có

Không tự giả định backend có các chức năng sau:

- Lịch theo ngày thực tế, `NonWorkingDay`, hoặc `AppointmentSlot`.
- Phân trang, tìm kiếm, lọc Shift.
- API chỉ gửi phần thay đổi của checkbox.
- Tự động xử lý ca qua đêm hoặc chống overlap Shift ở database.
- Khôi phục riêng một `DoctorShift` đã xóa mềm.

