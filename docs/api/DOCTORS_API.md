# DOCTOR — FRONTEND HANDOFF (API ĐANG CHẠY)

## Mục tiêu

Tạo trang quản lý Bác sĩ và gọi đúng API hiện tại. Request tạo/sửa bác sĩ là **`multipart/form-data`** vì có upload ảnh; không gửi JSON.

```text
API base URL: https://localhost:7023
Fallback HTTP: http://localhost:5244
Base path: /api/doctors
```

## Điểm cần biết trước khi làm UI

Response Doctor hiện **không có `specialtyId` và tên chuyên khoa**. Vì vậy API hiện tại chưa thể tự điền đúng chuyên khoa trong form Sửa sau khi trang được tải lại. Không được tự đoán giá trị này. Cần bổ sung `specialtyId` vào response backend trước khi hoàn thiện màn Sửa.

Quy ước UI cho `gender`: `0 = Nam`, `1 = Nữ`. Backend chỉ chấp nhận số `0` hoặc `1`.

## Kiểu response hiện tại

```ts
interface Doctor {
  doctorId: number;
  fullName: string;
  phoneNumber: string | null;
  email: string | null;
  gender: 0 | 1;
  address: string | null;
  avatarUrl: string | null;
  careerStartDate: string; // YYYY-MM-DD
  experience: number; // backend tự tính
}
```

Ví dụ:

```json
{
  "doctorId": 1,
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0901234567",
  "email": "an@example.com",
  "gender": 0,
  "address": "Quận 1, TP.HCM",
  "avatarUrl": "https://...",
  "careerStartDate": "2015-09-01",
  "experience": 11
}
```

## API contract

### 1. Lấy danh sách

```http
GET /api/doctors
```

- **200 OK:** trả `Doctor[]`.
- Chỉ trả bác sĩ chưa bị soft delete.

### 2. Lấy chi tiết

```http
GET /api/doctors/{id}
```

- **200 OK:** trả một `Doctor`.
- **404 Not Found:**

```json
{ "message": "Doctor with ID 1 does not exist." }
```

### 3. Tạo bác sĩ

```http
POST /api/doctors
Content-Type: multipart/form-data
```

Gửi bằng `FormData`, với các key sau:

| Key | Kiểu gửi | Bắt buộc | Ví dụ |
| --- | --- | --- | --- |
| `fullName` | text | Có | `Nguyễn Văn An` |
| `phoneNumber` | text | Không | `0901234567` |
| `email` | text | Không | `an@example.com` |
| `gender` | text/number | Có | `0` hoặc `1` |
| `address` | text | Không | `Quận 1, TP.HCM` |
| `careerStartDate` | text | Có | `2015-09-01` |
| `specialtyId` | text/number | Có | `1` |
| `avatar` | file | Không | file ảnh |

- **201 Created:** trả object `Doctor`.
- Không tự đặt header `Content-Type` khi dùng `FormData`; browser/axios tự thêm boundary.

### 4. Sửa bác sĩ

```http
PUT /api/doctors/{id}
Content-Type: multipart/form-data
```

Gửi tất cả key của Create, thêm:

| Key | Kiểu gửi | Quy tắc |
| --- | --- | --- |
| `removeAvatar` | `true` / `false` | Mặc định `false`. Gửi `true` khi muốn xóa ảnh cũ. |

Quy tắc ảnh khi sửa:

- Có ảnh mới: gửi `avatar`, `removeAvatar=false`.
- Xóa ảnh cũ: không gửi `avatar`, gửi `removeAvatar=true`.
- Giữ ảnh cũ: không gửi `avatar`, gửi `removeAvatar=false`.
- Không gửi đồng thời ảnh mới và `removeAvatar=true`; backend trả `400`.

- **200 OK:** trả object `Doctor` sau khi sửa.
- **404 Not Found:** ID không tồn tại/đã soft delete.

### 5. Xóa bác sĩ

```http
DELETE /api/doctors/{id}
```

- **204 No Content:** thành công, không có body.
- **404 Not Found:** ID không tồn tại.
- Đây là soft delete. Sau thành công, xóa item khỏi bảng hiện tại; `GET /api/doctors` sẽ không trả item đó nữa.

## Validation form phải áp dụng

| Field | Ràng buộc |
| --- | --- |
| `fullName` | Bắt buộc; trim; từ 3–100 ký tự. |
| `phoneNumber` | Không bắt buộc; tối đa 20 ký tự. |
| `email` | Không bắt buộc; đúng định dạng email; tối đa 50 ký tự. Email là duy nhất trong database. |
| `gender` | Bắt buộc; chỉ `0` hoặc `1`. |
| `address` | Không bắt buộc; tối đa 255 ký tự. |
| `careerStartDate` | Bắt buộc; `YYYY-MM-DD`; không lớn hơn ngày hiện tại. |
| `specialtyId` | Bắt buộc; số nguyên dương; phải tồn tại. Lấy danh sách chọn từ `GET /api/specialties`. |
| `avatar` | Không bắt buộc; JPEG/PNG/WebP; tối đa 5 MB. |

UI chỉ nên hiển thị chuyên khoa `Active` trong dropdown tạo mới. Backend hiện chỉ kiểm tra ID có tồn tại, nên không tự chặn ID `Inactive`.

## Lỗi cần xử lý

- **400:** validation lỗi. Response ASP.NET có `errors`; gắn lỗi vào field tương ứng.
- **404:** báo “Bác sĩ không tồn tại hoặc đã bị xóa.”
- **503:** không lưu được ảnh; giữ dữ liệu form và báo người dùng thử lại.
- **500:** không lưu được database (ví dụ email bị trùng); hiện lỗi chung và giữ dữ liệu form.

## UI cần tạo

1. Bảng: ảnh đại diện, họ tên, email, điện thoại, giới tính, ngày bắt đầu nghề, kinh nghiệm, thao tác.
2. Form Thêm/Sửa gồm đầy đủ field trong bảng request; dùng `FormData` để submit.
3. Dropdown chuyên khoa gọi `GET /api/specialties`, hiển thị `name`, value là `specialtyId`.
4. Preview ảnh trước khi submit; kiểm tra type/size ở client trước khi gửi.
5. Nút Xóa có xác nhận; gọi DELETE rồi bỏ item khỏi danh sách.
6. Disable nút Lưu khi request đang chạy.

Không tự tạo API tìm kiếm, phân trang, khôi phục bác sĩ, hoặc upload ảnh riêng: backend hiện chưa có các API đó.
