# Tài khoản, phân quyền, hồ sơ của tôi và lịch sử lịch hẹn

## Phạm vi phiên bản này

Tài khoản dùng email đăng nhập (không phân biệt hoa/thường), mật khẩu được hash bằng
ASP.NET Core PasswordHasher. Mỗi tài khoản có đúng một vai trò. Tài khoản Patient liên kết
đúng một Patient; Doctor liên kết đúng một Doctor; Admin/Receptionist không liên kết hồ sơ.
Một hồ sơ chỉ được liên kết một tài khoản, kể cả tài khoản đó đang bị khóa.

Đăng ký công khai tạo **hồ sơ bệnh nhân mới** cùng tài khoản trong một transaction.
Không tự nhận hồ sơ cũ bằng cách khớp email/số điện thoại. Admin xác minh danh tính ngoài hệ
thống trước khi liên kết một tài khoản với hồ sơ có sẵn.

Phiên đăng nhập dùng **Cookie Authentication của ASP.NET Core**, hết hạn cố định sau 8 giờ.
`SignInAsync` tạo cookie `__Host-Clinic.Auth` đã được Data Protection mã hóa và bảo vệ
chống sửa đổi. Cookie là HttpOnly, Secure, SameSite=Lax, Path=/, không có Domain và không
lưu lâu dài (`IsPersistent=false`). Frontend không đọc cookie, không giữ accessToken.
Database giữ UserSession để thu hồi phiên ngay; SessionId chỉ nằm trong cookie được bảo vệ.
Không dùng ASP.NET Core Session (`AddSession`) cho đăng nhập.

Mọi POST/PUT/PATCH/DELETE cần chống CSRF, kể cả đăng nhập, đăng ký và upload:

1. Gọi `GET /api/auth/csrf` để nhận `{ "requestToken": "..." }` và cookie CSRF.
2. Gửi `requestToken` qua header `X-CSRF-TOKEN`; trình duyệt tự gửi cookie.
3. Lấy lại requestToken sau đăng nhập/đăng xuất/đổi mật khẩu vì danh tính đã thay đổi.

Server đọc trạng thái tài khoản, vai trò và liên kết hiện tại ở mỗi request.
Đăng xuất xóa phiên hiện tại; đổi mật khẩu hoặc sửa quyền/liên kết/IsActive xóa tất cả phiên
của tài khoản. Hồ sơ bác sĩ/bệnh nhân đã xóa mềm cũng không được đăng nhập hoặc sử dụng phiên cũ.
Chưa có refresh token, xác minh email, quên mật khẩu qua email hay MFA trong phạm vi này.

## Bảng quyền

| Chức năng | Khách | Patient | Doctor | Receptionist | Admin |
|---|---|---|---|---|---|
| Xem bác sĩ/chuyên khoa/slot trống | Có | Có | Có | Có | Có |
| Xem tài khoản và hồ sơ của tôi | Không | Có | Có | Có | Có |
| Sửa hồ sơ bệnh nhân của tôi | Không | Có | Không | Không | Không |
| Liệt kê/quản lý hồ sơ bệnh nhân | Không | Chỉ đọc/sửa hồ sơ mình | Không | Có | Có |
| Xem lịch hẹn và lịch sử | Không | Của mình | Bác sĩ đang được phân công | Tất cả | Tất cả |
| Đặt/đổi/hủy lịch | Không | Của mình | Không | Tất cả | Tất cả |
| Đánh dấu đã khám/vắng mặt | Không | Không | Lịch được phân công | Tất cả | Tất cả |
| Quản lý tài khoản/vai trò/liên kết | Không | Không | Không | Không | Có |
| Ghi danh mục bác sĩ/chuyên khoa/ca/ngày nghỉ | Không | Không | Không | Không | Có |

GET ca làm, phân công tuần và ngày nghỉ thuộc API quản trị. Luồng khách dùng API slot trống.
Hồ sơ chuyên môn bác sĩ được Admin cập nhật qua API doctors; GET /me/profile cho bác sĩ là chỉ đọc.

## Endpoint

| Method | Route | Kết quả |
|---|---|---|
| POST | /api/auth/register | 201 AccountDto, không tự đăng nhập |
| GET | /api/auth/csrf | 200 requestToken và cookie CSRF; cho phép anonymous |
| POST | /api/auth/login | 200 expiresAt, account và Set-Cookie; không trả token đăng nhập |
| POST | /api/auth/logout | 204, thu hồi phiên hiện tại |
| POST | /api/auth/change-password | 204, thu hồi mọi phiên; cần đăng nhập lại |
| GET | /api/me/profile | 200 gồm account, patient hoặc doctor; staff có cả hai hồ sơ null |
| PUT | /api/me/profile | 200 PatientDto, chỉ Patient sửa hồ sơ của mình |
| GET | /api/me/appointments | 200 AppointmentPageDto; chỉ Patient/Doctor |
| GET | /api/accounts?page=1&pageSize=20 | Admin; danh sách AccountDto, pageSize tối đa 100 |
| POST | /api/accounts | Admin tạo tài khoản và liên kết hồ sơ đã tồn tại |
| PUT | /api/accounts/{guid}/access | Admin thay thế toàn bộ vai trò/liên kết/IsActive |
| GET | /api/appointments/{id}/history?page=1&pageSize=50 | Lịch sử mới nhất trước, pageSize tối đa 100 |

Các API lịch hẹn cũ vẫn giữ route và body. GET /api/appointments được tự giới hạn theo
người đăng nhập. GET /api/me/appointments cũng hỗ trợ fromDate, toDate, status, specialtyId,
doctorId, patientId, page, pageSize. Nếu Patient cố lọc PatientId khác hoặc Doctor cố lọc
DoctorId khác, trả 403. Bộ lọc và TotalCount đều được tính trong phạm vi đã cấp quyền.
Admin/Receptionist dùng /api/appointments, không dùng /me/appointments.

## Đăng ký và đăng nhập

```json
{
  "email": "patient@example.com",
  "password": "Replace-with-your-own-password",
  "profile": {
    "fullName": "Nguyễn Văn An",
    "dateOfBirth": "2000-01-01",
    "gender": 0,
    "phoneNumber": "0901234567",
    "email": "patient@example.com",
    "address": "TP. Hồ Chí Minh"
  }
}
```

Mật khẩu mới dài 12–128 ký tự. Email đăng nhập có thể khác email liên hệ trong hồ sơ.
PUT /me/profile dùng body giống UpdatePatientRequest (các thuộc tính bên trong profile ở trên).
Sửa hồ sơ không thay đổi email đăng nhập hoặc vai trò.

POST /auth/login:

```json
{ "email": "patient@example.com", "password": "Replace-with-your-own-password" }
```

POST /auth/change-password:

```json
{ "currentPassword": "Current-password", "newPassword": "Different-new-password" }
```

Sai mật khẩu 5 lần sẽ khóa đăng nhập 15 phút. Thông báo đăng nhập không phân biệt email
không tồn tại, mật khẩu sai hay tài khoản bị khóa. Các endpoint register/login/change-password
giới hạn chung 20 request/phút/IP mỗi API instance (cấu hình Authentication:RequestsPerMinute).
Sau reverse proxy cần cấu hình trusted forwarded headers trước khi dùng IP client;
không tin X-Forwarded-For tùy ý. Khi chạy nhiều instance, limiter hiện tại không cộng dồn giữa instance.

## Admin tạo và liên kết tài khoản

POST /api/accounts cho hồ sơ bác sĩ đã có:

```json
{
  "email": "doctor@example.com",
  "password": "Replace-with-your-own-password",
  "displayName": "Bác sĩ Nguyễn Văn Bình",
  "role": "Doctor",
  "doctorId": 5,
  "patientId": null
}
```

Patient phải có patientId và doctorId=null; Doctor phải có doctorId và patientId=null.
Receptionist/Admin phải để cả hai null. Enum nhận tên Patient, Doctor, Receptionist, Admin.
Hồ sơ không tồn tại/đã xóa trả 404; email hoặc liên kết đã được dùng trả 409.

PUT /api/accounts/{guid}/access để ngừng tài khoản:

```json
{ "role": "Doctor", "doctorId": 5, "patientId": null, "isActive": false }
```

Phải gửi đầy đủ trạng thái mới, không phải PATCH. Không thể khóa hoặc hạ vai trò của Admin
hoạt động cuối cùng. Đổi liên kết thu hồi mọi phiên cũ; lần đăng nhập mới chỉ thấy hồ sơ/lịch
của liên kết mới. Đây là thao tác quản trị có ảnh hưởng quyền truy cập.

## Lịch sử lịch hẹn

Tự ghi Created, Rescheduled, Cancelled, Completed, NoShow. Mỗi dòng gồm:

- appointmentHistoryId, appointmentId, occurredAt (UTC).
- actorAccountId, actorDisplayName, actorRole được chụp tại thời điểm thao tác.
- before và after: slot, bác sĩ, chuyên khoa, ngày, giờ, trạng thái, lý do hủy.

Created có before=null. Retry hủy/kết thúc cùng trạng thái hoặc đổi về slot đang giữ không
sinh thêm lịch sử. Nếu lưu lịch sử thất bại, cả thao tác lịch hẹn và thay đổi slot rollback.
Không có endpoint sửa/xóa lịch sử. Phân quyền lịch sử giống quyền đọc lịch hẹn hiện tại;
bác sĩ cũ không còn quyền đọc sau khi lịch đã chuyển sang bác sĩ khác.

Migration không dựng lại lịch sử cũ vì không có thông tin đáng tin cậy về người thực hiện
và các giá trị trước đây. Lịch đã tồn tại vẫn giữ nguyên, lịch sử bắt đầu từ thao tác mới.

## Tích hợp frontend

Chạy API bằng profile HTTPS: `dotnet run --project WebDatLichKhamBenh.Api --launch-profile https`.
Frontend local cũng cần HTTPS khi gọi API HTTPS để cookie SameSite=Lax hoạt động cùng site.
Dùng cùng hostname `localhost`, không trộn `127.0.0.1` với `localhost`.
CORS chỉ cho phép các origin đã khai báo trong Program.cs và bật AllowCredentials.
Khi triển khai, thay bằng origin frontend thực tế; không dùng wildcard với credentials.
Thiết kế này dành cho frontend/API cùng site. Hai domain khác site cần thiết kế cookie/CORS
riêng; không chỉ đổi URL rồi kỳ vọng cookie vẫn được gửi.

```javascript
const apiUrl = "https://localhost:7023";
let csrfToken;

async function refreshCsrfToken() {
  const response = await fetch(`${apiUrl}/api/auth/csrf`, {
    credentials: "include"
  });
  if (!response.ok) throw new Error("Không lấy được CSRF token");
  csrfToken = (await response.json()).requestToken;
}

async function login(email, password) {
  await refreshCsrfToken();
  const response = await fetch(`${apiUrl}/api/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": csrfToken
    },
    body: JSON.stringify({ email, password })
  });
  if (!response.ok) throw new Error("Đăng nhập thất bại");
  const result = await response.json(); // expiresAt, account
  await refreshCsrfToken(); // Token phải khớp danh tính vừa đăng nhập.
  return result;
}

async function logout() {
  const response = await fetch(`${apiUrl}/api/auth/logout`, {
    method: "POST",
    credentials: "include",
    headers: { "X-CSRF-TOKEN": csrfToken }
  });
  if (!response.ok) throw new Error("Đăng xuất thất bại");
  await refreshCsrfToken();
}

async function getMyProfile() {
  const response = await fetch(`${apiUrl}/api/me/profile`, {
    credentials: "include"
  });
  if (!response.ok) throw new Error("Không lấy được hồ sơ");
  return response.json();
}
```

- CSRF token có thể giữ trong bộ nhớ JavaScript; cookie đăng nhập là HttpOnly.
- Không thêm Authorization: Bearer, không lưu thông tin đăng nhập vào localStorage.
- Patient đặt lịch dùng patientId từ `/api/me/profile`; server vẫn kiểm tra quyền sở hữu.
- 400 có thông báo CSRF: lấy lại token. 401: đăng nhập lại. 403: không có quyền.
  404: không tồn tại hoặc không được phép biết dữ liệu. 409: xung đột. 429: thử lại sau.
- Không cache chung response cá nhân giữa các tài khoản.

**Swagger:** mở HTTPS, gọi GET /api/auth/csrf, copy requestToken vào nút Authorize (CSRF),
rồi gọi login. Gọi csrf và Authorize lại sau login. Swagger dùng cookie do trình duyệt
lưu tự động; ô Authorize chỉ chứa CSRF token. GET đọc dữ liệu không cần CSRF token.

**Postman / HTTP client:** bật cookie jar, dùng HTTPS và gửi header X-CSRF-TOKEN.
Không tắt kiểm tra CSRF cho riêng công cụ test.

## Khởi tạo và migration

```powershell
dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api
# Đặt BootstrapAdmin__Email và BootstrapAdmin__Password trong môi trường tiến trình trước khi chạy:
dotnet run --project WebDatLichKhamBenh.Api --no-launch-profile -- --bootstrap-admin
```

Lệnh bootstrap chỉ chạy được khi chưa có Admin hoạt động và không có HTTP endpoint tương ứng.
Không đưa mật khẩu vào source hoặc tham số dòng lệnh. Xóa biến môi trường bootstrap sau khi dùng.
Trên máy hiện tại, thông tin Admin khởi tạo nằm tại .local/initial-admin.json (Git bỏ qua).
Đổi mật khẩu sau lần đăng nhập đầu; file ban đầu không được tự cập nhật khi đổi mật khẩu.

Factory EF đọc appsettings của API, appsettings theo ASPNETCORE_ENVIRONMENT, user-secrets khi
Development và biến môi trường (ưu tiên cao nhất). Có thể đặt ConnectionStrings__DefaultConnection
để chọn database đích. Migration `UseCookieAuthentication` xóa các phiên đăng nhập cũ,
bỏ cột TokenHash và index tương ứng; tài khoản, mật khẩu, hồ sơ và lịch hẹn vẫn giữ nguyên.
Cả nâng cấp lẫn rollback đều yêu cầu đăng nhập lại. Code mới cần chạy cùng schema mới.

Cookie còn phụ thuộc Data Protection keys: khi triển khai container cần lưu key ring ở
volume bền vững; nhiều instance cần dùng chung key ring và application name. Bảo vệ quyền
truy cập thư mục keys. Khi chạy sau reverse proxy HTTPS, cấu hình trusted forwarded headers
trước HTTPS redirection/authentication để server nhận đúng scheme. Chạy trực tiếp Kestrel
HTTPS trong môi trường học tập không cần cấu hình proxy.

## Kiểm thử

```powershell
# Trỏ tới PostgreSQL dành riêng cho test, tài khoản có quyền CREATE DATABASE:
$env:APPOINTMENT_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Pooling=false'
dotnet test tests/WebDatLichKhamBenh.IntegrationTests/WebDatLichKhamBenh.IntegrationTests.csproj
```

Fixture tạo database appointment_tests_<guid>, chạy migration, bootstrap Admin test rồi xóa
chính database này khi kết thúc. Không dùng tên database nghiệp vụ làm database test.
Các test đăng nhập thật qua HTTP; không giả lập auth handler hoặc bỏ qua kiểm tra quyền.

Đọc [hướng dẫn học luồng Cookie Authentication](../learning/COOKIE_AUTHENTICATION.md) để theo dõi từng lớp và lý do thiết kế.
