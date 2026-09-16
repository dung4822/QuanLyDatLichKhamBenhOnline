# API đặt lịch và quản lý danh sách lịch khám

## Phạm vi

Đặt thành công là xác nhận ngay, chưa có bước duyệt hay thanh toán.
Giờ khám dùng giờ Việt Nam; các timestamp tạo/cập nhật/hủy lưu UTC.
Một slot dài 30 phút; đặt trong hôm nay đến hôm nay + 6 ngày và phải chưa đến giờ bắt đầu.

Các API bên dưới yêu cầu bearer token. Patient chỉ đọc/đặt/đổi/hủy lịch của mình;
Doctor chỉ đọc và cập nhật kết quả lịch được phân công; Admin/Receptionist quản lý toàn bộ.
Chi tiết đăng nhập, API /me và lịch sử: [ACCOUNTS_AUTH_API.md](ACCOUNTS_AUTH_API.md).

## Endpoint

| Method | Route | Kết quả |
|---|---|---|
| POST | /api/appointments | Tạo lịch, 201 Created và Location |
| GET | /api/appointments/{id} | Chi tiết, 200; không tồn tại 404 |
| GET | /api/appointments | Danh sách có lọc, phân trang và giới hạn quyền |
| GET | /api/appointments/{id}/history | Lịch sử thay đổi và người thực hiện |
| PATCH | /api/appointments/{id}/reschedule | Đổi bác sĩ/ngày/giờ, trả AppointmentDto với 200 |
| PATCH | /api/appointments/{id}/cancel | Hủy lịch; body JSON có thể là {} |
| PATCH | /api/appointments/{id}/complete | Đánh dấu đã khám; không cần body |
| PATCH | /api/appointments/{id}/no-show | Đánh dấu không đến; không cần body |

Không có DELETE lịch hẹn trong phiên bản này.

## Đổi lịch khám

```http
PATCH /api/appointments/1/reschedule
Content-Type: application/json
```

```json
{
  "appointmentSlotId": 110
}
```

- Chọn slot trống từ `GET /api/doctors/{doctorId}/appointment-slots`; có thể chọn bác sĩ khác.
- Chỉ đổi lịch Confirmed, giờ bắt đầu của lịch hiện tại phải còn ít nhất 2 tiếng
  (đúng 2 tiếng được phép, dưới 2 tiếng trả 409), tính theo giờ Việt Nam tại server.
- Mốc 2 tiếng áp dụng cho lịch hiện tại. Slot mới phải chưa bắt đầu, nằm trong 7 ngày
  và thỏa toàn bộ điều kiện ca làm/ngày nghỉ/trạng thái như khi đặt lịch.
- `appointmentSlotId` phải > 0, nếu không trả 400. Lịch hẹn, slot hoặc bệnh nhân không tồn tại trả 404.
- Cho phép đổi bác sĩ, kể cả khác chuyên khoa. Backend tự lấy bác sĩ, chuyên khoa, ngày giờ từ slot mới.
- Kiểm tra trùng lịch bác sĩ và bệnh nhân, loại trừ chính lịch đang đổi. Vi phạm trả 409.
- Giữ nguyên AppointmentId, bệnh nhân, lý do khám, CreatedAt và trạng thái Confirmed;
  cập nhật slot, thông tin bác sĩ/chuyên khoa, ngày giờ và UpdatedAt.
- Giữ slot mới, cập nhật lịch và trả slot cũ trong cùng transaction; lỗi ở bất kỳ bước nào
  sẽ rollback, giữ nguyên lịch cũ. Slot cũ chỉ mở lại nếu còn hợp lệ và không trùng lịch khác.
- Gửi lại slot hiện tại trả 200, không đổi dữ liệu, nếu lịch vẫn Confirmed và còn đủ 2 tiếng.
- Ghi lịch sử trước/sau và tài khoản thực hiện trong cùng transaction. Lịch sử có từ khi
  chức năng này được triển khai; không dựng lại các lần đổi cũ.

## Tạo lịch

```http
POST /api/appointments
Content-Type: application/json
```

```json
{
  "patientId": 12,
  "appointmentSlotId": 105,
  "reason": "Đau đầu trong 3 ngày"
}
```

- Hai ID phải > 0; reason tùy chọn, tối đa 1.000 ký tự, được trim.
- Backend lấy bác sĩ, chuyên khoa, ngày, giờ từ slot, không nhận các trường đó từ client.
- Bệnh nhân và slot không tồn tại trả 404; bệnh nhân đã xóa mềm cũng trả 404.
- Trả 409 nếu bác sĩ đã xóa, chuyên khoa không Active, giờ quá hạn/ngoài 7 ngày,
  phân công bị xóa, ca ngừng/xóa/đổi giờ, ngày nghỉ, slot không Available hoặc trùng lịch.
- Chuyên khoa Active được so sánh không phân biệt hoa/thường.
- Kiểm tra lại ngày nghỉ cả viện (DoctorId null) và nghỉ riêng bác sĩ.
- Slot phải đúng lưới 30 phút tính từ đầu ca, đúng thứ và nằm trọn trong ca hiện tại.
- Kiểm tra chồng giờ cả bác sĩ và bệnh nhân, kể cả khác slot hoặc khác bác sĩ.
- Hai khoảng giao nhau khi start mới < end cũ và start cũ < end mới.
  08:00–08:30 và 08:30–09:00 không giao nhau.
- Khi nhận 409, UI hiển thị detail và tải lại slot trống.
- Retry POST không có idempotency key; nếu mất response, xem lại danh sách trước khi tạo yêu cầu mới.

Ví dụ response:

```json
{
  "appointmentId": 1,
  "patientId": 12,
  "patientFullName": "Nguyễn Văn An",
  "patientPhoneNumber": "0901234567",
  "appointmentSlotId": 105,
  "doctorId": 5,
  "doctorFullName": "Trần Minh",
  "specialtyId": 2,
  "specialtyName": "Nội tổng quát",
  "date": "2026-09-13",
  "startTime": "08:00:00",
  "endTime": "08:30:00",
  "reason": "Đau đầu trong 3 ngày",
  "status": "Confirmed",
  "createdAt": "2026-09-12T07:00:00Z",
  "updatedAt": null,
  "cancelledAt": null,
  "cancellationReason": null
}
```

Tên bệnh nhân, số điện thoại, tên bác sĩ, chuyên khoa, ngày và giờ là bản chụp lúc đặt.
Các thông tin này không đổi khi cập nhật hồ sơ hoặc tái sử dụng slot sau khi hủy.
Muốn xem thông tin liên hệ mới nhất, truy vấn hồ sơ bệnh nhân tương ứng.

## Danh sách quản lý

```http
GET /api/appointments?doctorId=5&fromDate=2026-09-13&toDate=2026-09-13&status=Confirmed&page=1&pageSize=20
```

Các bộ lọc tùy chọn được kết hợp bằng AND:

| Query | Quy tắc |
|---|---|
| patientId, doctorId, specialtyId | ID > 0; không có kết quả thì items rỗng, không trả 404 |
| fromDate, toDate | Bao gồm cả hai đầu mốc; toDate >= fromDate khi gửi cả hai |
| status | Confirmed, Cancelled, Completed, NoShow |
| page | Mặc định 1; 1 đến 1.000.000 |
| pageSize | Mặc định 20; 1 đến 100 |

Không giới hạn danh sách vào 7 ngày vì quản trị cần xem lịch sử.
Sắp xếp ngày, giờ bắt đầu, ID tăng dần. Để xem lịch hôm nay, gửi cả fromDate và toDate bằng hôm nay.

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 20
}
```

Trong items là các object như response tạo lịch. totalCount là tổng kết quả trước phân trang.
Hiển thị các cột: mã hẹn, ngày/giờ, bệnh nhân, điện thoại lúc đặt, bác sĩ, chuyên khoa,
lý do và trạng thái. Cho phép mở chi tiết và dùng các thao tác trạng thái phù hợp.

## Trạng thái và hủy

- Confirmed → Cancelled: chỉ trước giờ bắt đầu; body {"reason":"Bận việc"}, lý do tùy chọn tối đa 500 ký tự.
- Confirmed → Completed hoặc NoShow: chỉ từ giờ kết thúc của lịch khám trở đi.
- Gọi lại đúng thao tác đã hoàn thành trả 200 với dữ liệu hiện tại (không đổi timestamps/lý do).
- Không chuyển từ Cancelled/Completed/NoShow sang trạng thái khác; vi phạm trả 409.
- Hủy lịch cũ lần nữa không được mở slot đã có người khác đặt lại.
- Hủy cập nhật Appointment và slot trong cùng transaction. Slot chỉ Available lại khi vẫn hợp lệ;
  nếu ca đã đổi, bác sĩ nghỉ hoặc chuyên khoa ngừng thì thành Unavailable.
- Completed/NoShow không mở lại slot; lịch hẹn được giữ để xem lịch sử.

## Tương tác với API lịch làm hiện có

Khi có Confirmed chưa kết thúc (bao gồm đang trong giờ khám), các thao tác sau trả 409:

- Xóa bác sĩ, đổi chuyên khoa bác sĩ hoặc xóa bệnh nhân liên quan.
- Ngừng chuyên khoa đang có lịch hẹn.
- Đổi thứ/giờ/trạng thái hoặc xóa ca có lịch hẹn; đổi tên ca vẫn được.
- Bỏ phân công ca đang có lịch hẹn khỏi lịch tuần.
- Tạo/sửa ngày nghỉ ảnh hưởng lịch hẹn của bác sĩ hoặc toàn viện.

Cần xử lý lịch hẹn trước. Lịch đã đến giờ không hủy được; nếu cần nghiệp vụ hủy khẩn cấp
hoặc đổi bác sĩ lúc đang khám thì bổ sung thành thao tác riêng sau.

## Transaction và chống tranh chấp

Infrastructure dùng một PostgreSQL transaction advisory lock chung cho các thao tác ghi lịch:
đặt/đổi/hủy/kết thúc hẹn, đồng bộ slot, sửa ca, lịch tuần, ngày nghỉ và các thay đổi hồ sơ liên quan.
Khóa được lấy trước khi đọc điều kiện; lệnh đọc sau khi lấy khóa dùng dữ liệu mới nhất của READ COMMITTED.
Các lời gọi lồng nhau dùng chung transaction. Nếu đồng bộ slot thất bại, thay đổi lịch làm cũng rollback.

Chiếm slot dùng UPDATE có điều kiện Status = Available và kiểm tra số dòng được cập nhật.
Insert Appointment và chiếm slot cùng transaction. Database thêm unique index cho
AppointmentSlotId với Status khác Cancelled; hủy hoặc đổi sang slot khác sẽ giải phóng liên kết cũ.

Đồng bộ slot giữ nguyên Booked và đánh dấu các giờ mới giao với slot Booked là Unavailable.
Lịch hẹn còn được kiểm tra chồng bằng thời gian chụp lại trong bảng Appointments.

Khóa chung đơn giản và phù hợp phiên bản đầu; các thao tác ghi phải chờ nhau, nên cần đo tải trước khi
nâng lên khóa theo bác sĩ/bệnh nhân. Chỉnh SQL trực tiếp không tuân theo khóa này;
database chỉ bảo vệ trùng slot, còn chồng khoảng giờ và quy tắc lịch làm thuộc luồng service.

Tham khảo: [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)
và [EF Core ExecuteUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).

## Áp dụng migration

Migration mới: AddAppointments. Đã kiểm tra toàn bộ chuỗi migration trên PostgreSQL riêng của test.
Đã áp dụng vào database phát triển webdatlich tại localhost:5432 ngày 2026-09-12.
Với database khác, dùng lệnh bên dưới để cập nhật.

AppDbContextFactory hiện có connection thiết kế cố định từ trước. Khi cập nhật database,
chỉ rõ connection của database phát triển muốn sử dụng bằng --connection để tránh chọn sai database:

```powershell
dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api --connection "<connection string database phát triển>"
```

Chạy migration trước khi khởi động API. Không cần tạo lại migration.

## Chạy integration tests

Test dùng PostgreSQL thật, tạo database mới tên appointment_tests_<guid>, chạy migrations,
gửi HTTP qua ASP.NET Core test host rồi chỉ xóa database của chính lần test đó.
Không dùng connection của ứng dụng. Cần tài khoản test có quyền tạo database.

```powershell
$env:APPOINTMENT_TEST_POSTGRES = "Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres"
dotnet test tests/WebDatLichKhamBenh.IntegrationTests/WebDatLichKhamBenh.IntegrationTests.csproj
```

Thay host/port và cơ chế xác thực bằng PostgreSQL dành cho kiểm thử của bạn.
Mặc định nếu không đặt biến môi trường là localhost:55439, user postgres.
Các test kiểm tra HTTP, lọc/phân trang, validation, trạng thái, snapshot lịch sử, tranh chấp đặt lịch,
ràng buộc database, rollback khi insert/sync lỗi và xung đột với thay đổi lịch làm.
