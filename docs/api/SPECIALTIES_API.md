# SPECIALTY — FRONTEND HANDOFF (API ĐANG CHẠY)

## Mục tiêu

Tạo trang quản lý **Chuyên khoa** và gọi đúng backend hiện tại. Không tự thêm phân trang, tìm kiếm API, đăng nhập, hay endpoint khác.

## Kết nối

```text
API base URL: https://localhost:7023
Fallback HTTP: http://localhost:5244
Base path: /api/specialties
Content-Type: application/json
```

API dùng JSON `camelCase`.

## Kiểu dữ liệu dùng ở frontend

```ts
type SpecialtyStatus = "Active" | "Inactive";

interface Specialty {
  specialtyId: number;
  name: string;
  description: string | null;
  status: string;
  createdAt: string;
  updatedAt: string | null;
}

interface SaveSpecialtyRequest {
  name: string;
  description: string | null;
  status: SpecialtyStatus;
}
```

> UI chỉ gửi `Active` hoặc `Inactive`. Backend hiện lưu `status` dưới dạng chuỗi.

## API contract

### 1. Lấy danh sách

```http
GET /api/specialties
```

**200 OK** — trả mảng, bao gồm cả chuyên khoa `Inactive`:

```json
[
  {
    "specialtyId": 1,
    "name": "Tim mạch",
    "description": "Khám và điều trị bệnh lý tim mạch.",
    "status": "Active",
    "createdAt": "2026-08-08T03:20:14.123Z",
    "updatedAt": null
  }
]
```

### 2. Lấy chi tiết

```http
GET /api/specialties/{id}
```

Ví dụ: `GET /api/specialties/1`.

- **200 OK:** trả một object `Specialty`.
- **404 Not Found:** ID không tồn tại.

### 3. Tạo chuyên khoa

```http
POST /api/specialties
Content-Type: application/json
```

```json
{
  "name": "Da liễu",
  "description": "Khám các bệnh về da, tóc và móng.",
  "status": "Active"
}
```

- **201 Created:** trả object `Specialty` vừa tạo.
- **400 Bad Request:** body không hợp lệ.

### 4. Sửa chuyên khoa

```http
PUT /api/specialties/{id}
Content-Type: application/json
```

```json
{
  "name": "Da liễu",
  "description": null,
  "status": "Inactive"
}
```

- **200 OK:** trả object `Specialty` sau khi sửa.
- **404 Not Found:** ID không tồn tại.
- Khi sửa, luôn gửi đủ `name`, `description`, `status`. Muốn bỏ mô tả thì gửi `description: null`.

### 5. Ngừng sử dụng chuyên khoa

```http
DELETE /api/specialties/{id}
```

- **204 No Content:** thành công, không có JSON response.
- **404 Not Found:** ID không tồn tại.
- Đây là **soft delete**: backend chỉ đổi `status` thành `Inactive`; item vẫn xuất hiện khi gọi `GET /api/specialties`.

## Ràng buộc form phải áp dụng

| Field | UI gửi | Giới hạn an toàn theo database |
| --- | --- | --- |
| `name` | Bắt buộc; trim trước khi gửi | Tối đa **50** ký tự; không gửi chuỗi rỗng/chỉ khoảng trắng. |
| `description` | Có thể `null` | Tối đa **100** ký tự; rỗng thì gửi `null`. |
| `status` | Bắt buộc | Dropdown chỉ có `Active`, `Inactive`. |

Tên chuyên khoa phải là duy nhất trong database. Nếu backend trả lỗi server khi tên trùng, UI hiện thông báo chung: **“Tên chuyên khoa đã tồn tại hoặc dữ liệu không hợp lệ.”**

## UI cần tạo

1. Bảng danh sách: `ID`, `Tên chuyên khoa`, `Mô tả`, `Trạng thái`, `Ngày tạo`, `Ngày cập nhật`, `Thao tác`.
2. Nút **Thêm chuyên khoa** mở form gồm `name`, `description`, `status`.
3. Nút **Sửa** nạp dữ liệu item vào cùng form và gọi `PUT`.
4. Nút **Ngừng sử dụng** chỉ hiển thị khi `status === "Active"`; xác nhận trước khi gọi `DELETE`.
5. Item `Active` có badge xanh, `Inactive` có badge xám/đỏ. Item inactive vẫn nằm trong bảng quản trị.
6. Disable nút Lưu khi request đang chạy. Sau POST/PUT, dùng object server trả về để cập nhật UI. Sau DELETE, đổi item thành `Inactive` hoặc gọi lại API danh sách.

## Xử lý lỗi

- `400`: hiện lỗi form; response validation của ASP.NET có thể có `errors`.
- `404`: báo “Chuyên khoa không tồn tại hoặc đã bị thay đổi.”
- Mọi lỗi khác (kể cả lỗi tên trùng của backend hiện tại): hiện thông báo chung, không làm mất dữ liệu người dùng đang nhập.

Không được giả định có API tìm kiếm, phân trang, lọc theo status, API kích hoạt lại, hay xác thực vì backend hiện chưa cung cấp các phần đó. Kích hoạt lại một item được thực hiện bằng API `PUT` với `status: "Active"`.
