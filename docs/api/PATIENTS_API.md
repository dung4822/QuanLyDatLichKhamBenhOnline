# PATIENTS — FRONTEND HANDOFF

Tài liệu đối chiếu với code backend ngày 11/09/2026. Dùng để xây dựng màn hình quản lý bệnh nhân; các ví dụ là dữ liệu minh họa, không phải kết quả gọi API thực tế.

## Kết nối và quy ước

- Base URL theo profile local: `https://localhost:7023` hoặc `http://localhost:5244`. Backend có HTTPS redirection; ưu tiên HTTPS khi chạy profile HTTPS.
- CORS hiện cho phép `http://localhost:3000`, `https://localhost:3000`, `http://localhost:5173`, `https://localhost:5173`.
- POST/PUT dùng `Content-Type: application/json`.
- Response là object hoặc array trực tiếp, không bọc trong `data`.
- ID là số nguyên, field định danh là `patientId`.
- Chưa có xác thực/phân quyền cho các endpoint này.

## Kiểu dữ liệu frontend

```ts
type PatientRequest = {
  fullName: string;
  dateOfBirth: string; // YYYY-MM-DD, không kèm múi giờ
  gender: 0 | 1;
  phoneNumber: string;
  email: string | null;
  address: string | null;
};

type PatientDto = PatientRequest & {
  patientId: number;
  createdAt: string; // timestamp UTC
  updatedAt: string | null; // timestamp UTC
};
```

Đề xuất dùng cùng quy ước UI với tài liệu Doctors: `0 = Nam`, `1 = Nữ`. Code Patient chỉ quy định giá trị số 0/1, chưa định nghĩa enum đặt tên giới tính.

Ví dụ một `PatientDto`:

```json
{
  "patientId": 1,
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1998-05-20",
  "gender": 0,
  "phoneNumber": "0901234567",
  "email": "an@example.com",
  "address": "TP. Hồ Chí Minh",
  "createdAt": "2026-09-11T02:00:00Z",
  "updatedAt": null
}
```

`dateOfBirth` là ngày thuần: giữ chuỗi `YYYY-MM-DD` cho input date, tránh đổi qua UTC làm lệch ngày. `createdAt`/`updatedAt` có thể chuyển sang giờ địa phương để hiển thị. DTO không có `deletedAt`, avatar hoặc danh sách lịch hẹn.

## API contract

| Method | Endpoint | Thành công | Không tìm thấy |
| --- | --- | --- | --- |
| GET | `/api/patients` | `200` + `PatientDto[]` | Không áp dụng |
| GET | `/api/patients/{id}` | `200` + `PatientDto` | `404` |
| POST | `/api/patients` | `201` + `PatientDto`, có header `Location` trỏ đến API chi tiết | Không áp dụng |
| PUT | `/api/patients/{id}` | `200` + `PatientDto` | `404` |
| DELETE | `/api/patients/{id}` | `204`, không có body | `404` |

### Lấy danh sách và chi tiết

```http
GET /api/patients
GET /api/patients/1
```

Danh sách sắp xếp theo `patientId` tăng dần và không trả bệnh nhân đã xóa mềm. Khi chưa có dữ liệu, API danh sách trả `200` với `[]`. Không có query tìm kiếm, phân trang hoặc lọc.

GET chi tiết và PUT trả lỗi không tìm thấy dạng:

```json
{ "message": "Patient with ID 1 does not exist." }
```

### Tạo bệnh nhân

```http
POST /api/patients
Content-Type: application/json
```

```json
{
  "fullName": "Nguyễn Văn An",
  "dateOfBirth": "1998-05-20",
  "gender": 0,
  "phoneNumber": "0901234567",
  "email": "an@example.com",
  "address": "TP. Hồ Chí Minh"
}
```

Không gửi `patientId`, `createdAt`, `updatedAt`: backend tự tạo các giá trị này.

### Sửa bệnh nhân

```http
PUT /api/patients/1
Content-Type: application/json
```

Body có cùng cấu trúc với POST. Gửi đầy đủ dữ liệu form; đây là cập nhật toàn bộ các field nghiệp vụ, không phải PATCH. Gửi `email: null` hoặc `address: null` để xóa giá trị tùy chọn. Backend validate body trước khi tìm bệnh nhân, vì vậy body không hợp lệ có thể trả `400` dù ID không tồn tại.

### Xóa bệnh nhân

```http
DELETE /api/patients/1
```

Xóa mềm bằng cách đặt `DeletedAt` theo UTC. Sau thành công, bệnh nhân không còn xuất hiện trong GET danh sách và GET chi tiết trả `404`. Không gọi `response.json()` khi nhận `204`. DELETE với ID không tồn tại/đã xóa trả `404`; controller không cung cấp thông báo JSON riêng cho trường hợp này.

## Validation form

| Field | Quy tắc backend và cách gửi từ UI |
| --- | --- |
| `fullName` | Bắt buộc, sau trim dài 3–100 ký tự. Backend trim khi lưu. |
| `dateOfBirth` | Bắt buộc, ngày hợp lệ, không sau hôm nay theo ngày local của server (`DateTime.Today`). Không dùng `0001-01-01`. |
| `gender` | Số nguyên 0 hoặc 1; gửi số, không gửi nhãn hoặc chuỗi. Nên yêu cầu chọn rõ trên form: nếu bỏ field thì DTO mặc định là 0. |
| `phoneNumber` | Bắt buộc, không chỉ khoảng trắng, tối đa 20 ký tự trước khi backend trim. Gửi dạng chuỗi để giữ số 0 đầu. Backend chưa kiểm tra regex số điện thoại. |
| `email` | Tùy chọn. Khi không rỗng/không chỉ khoảng trắng: phải đúng định dạng email và tối đa 50 ký tự. UI nên trim và gửi `null` khi rỗng. |
| `address` | Tùy chọn, tối đa 255 ký tự trước khi backend trim. UI nên trim và gửi `null` khi rỗng. |

Backend chuẩn hóa email và địa chỉ rỗng/chỉ khoảng trắng thành `null`. Cấu hình Patient hiện không có unique index cho email hoặc số điện thoại; không mô tả chúng là bắt buộc duy nhất.

## Xử lý lỗi

- `400`: validation hoặc JSON/ngày/số sai định dạng. Đọc `errors` và gắn vào field.
- `404`: hiện “Bệnh nhân không tồn tại hoặc đã bị xóa.”; refresh danh sách nếu cần.
- `500`: hiện lỗi chung, giữ dữ liệu form và cho thử lại. Lỗi lưu dữ liệu được controller trả dạng Problem Details với `title: "Không thể lưu Patient."`, `detail: "Database không lưu được thay đổi."`.
- Lỗi mạng: giữ form và báo không kết nối được máy chủ.

Ví dụ phần dữ liệu cần đọc trong response validation (các field như `type`, `title`, `traceId` có thể được framework bổ sung):

```json
{
  "status": 400,
  "errors": {
    "FullName": ["FullName phải có từ 3 đến 100 ký tự."]
  }
}
```

Tên field từ FluentValidation là PascalCase (`FullName`, `DateOfBirth`, ...), còn dữ liệu JSON dùng camelCase. Map về field form; lỗi binding có thể mang key khác như `$.dateOfBirth`, cần có vùng lỗi chung cho key chưa map được. Không giả định mọi lỗi đều có `message` hoặc body luôn là JSON.

## UI cần tạo

1. Bảng danh sách: mã bệnh nhân, họ tên, ngày sinh, giới tính, điện thoại, email và thao tác. Địa chỉ/thời điểm tạo-cập nhật có thể đặt trong phần chi tiết.
2. Trạng thái tải, lỗi kèm nút thử lại, và danh sách rỗng.
3. Form Thêm/Sửa dùng chung các field của `PatientRequest`; nạp dữ liệu hiện tại khi sửa.
4. Nút Xem chi tiết và nút Xóa có xác nhận kèm tên bệnh nhân.
5. Disable nút Lưu/Xóa khi request đang chạy. Thành công POST/PUT thì dùng DTO server trả về hoặc tải lại danh sách; DELETE thì bỏ item khỏi bảng.
6. Giữ dữ liệu nhập khi request lỗi, hiển thị lỗi cạnh field và thông báo chung khi cần.

## Chức năng chưa có

Chưa có API tìm kiếm/phân trang, khôi phục bệnh nhân, upload ảnh, hồ sơ bệnh án, lịch sử khám, đăng nhập bệnh nhân hoặc liên kết Patient với Appointment. Nếu làm tìm kiếm cục bộ, chỉ lọc trên mảng GET đã tải và không gửi query chưa được hỗ trợ.

## Nguồn đối chiếu

- `WebDatLichKhamBenh.Api/Controllers/PatientsController.cs`
- `WebDatLichKhamBenh.Application/DTOs/Patients/` và `Services/PatientService.cs`
- `WebDatLichKhamBenh.Application/Validators/CreatePatientValidator.cs`, `UpdatePatientValidator.cs`
- `WebDatLichKhamBenh.Infrastructure/Repositories/PatientRepository.cs`
- `WebDatLichKhamBenh.Infrastructure/Persistence/Configurations/PatientConfiguration.cs`
- `WebDatLichKhamBenh.Api/Program.cs`, `Properties/launchSettings.json`
