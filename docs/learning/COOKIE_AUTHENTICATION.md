# Học luồng Cookie Authentication trong dự án

Mục tiêu của bản refactor: mỗi lớp có một việc rõ ràng, sử dụng tính năng xác thực có sẵn
của ASP.NET Core, và vẫn bảo vệ dữ liệu khám bệnh theo đúng quyền.

## 1. Phân biệt ba khái niệm trước khi đọc code

| Khái niệm | Câu hỏi nó giải quyết | Trong dự án |
|---|---|---|
| Authentication | Người gọi là ai? | Cookie Authentication và UserSession |
| Authorization | Người này được làm gì? | `[Authorize]`, role và kiểm tra quyền sở hữu trong service |
| CSRF protection | Request ghi dữ liệu có kèm token do ứng dụng cấp không? | `IAntiforgery` và header `X-CSRF-TOKEN` |

Cookie hợp lệ không có nghĩa là được xem mọi bệnh nhân. Patient chỉ được thao tác trên hồ sơ,
lịch hẹn của mình. CSRF token cũng không cấp quyền đăng nhập; nó bổ sung bảo vệ cho cookie.

## 2. Đọc code theo thứ tự này

Các đường dẫn bên dưới đều tính từ root solution:

1. `WebDatLichKhamBenh.Api/Controllers/AuthController.cs`: các endpoint và cách tạo/xóa cookie.
2. `WebDatLichKhamBenh.Application/Interfaces/Services/IAuthService.cs`: hợp đồng nghiệp vụ auth.
3. `WebDatLichKhamBenh.Application/Services/AuthService.cs`: đăng ký, kiểm tra mật khẩu, tạo/thu hồi phiên.
4. `WebDatLichKhamBenh.Application/Interfaces/Repositories/IAccountRepository.cs` và
   `WebDatLichKhamBenh.Infrastructure/Repositories/AccountRepository.cs`: cách đọc/ghi database.
5. `WebDatLichKhamBenh.Api/Extensions/AuthenticationExtensions.cs`: đăng ký Cookie Authentication,
   chống CSRF, và quyền mặc định.
6. `WebDatLichKhamBenh.Api/Security/ClinicCookieEvents.cs`: kiểm tra phiên ở mỗi request.
7. `WebDatLichKhamBenh.Api/Security/ClinicClaims.cs` và `CurrentUser.cs`: chuyển danh tính
   đã được xác thực thành dữ liệu mà Application có thể sử dụng.

`AuthService` không biết `HttpContext`, cookie, hay Entity Framework. Nó làm việc với interface
repository và trả DTO. `AuthController` gọi service, rồi dùng `SignInAsync`/`SignOutAsync`
để xử lý cơ chế HTTP. Đây là trách nhiệm của API, không phải logic kiểm tra mật khẩu.

`AccountService` phụ trách quản lý tài khoản/hồ sơ, đổi quyền và khởi tạo Admin. Nó không
còn xử lý đăng nhập. `AccountMappings.ToDto()` là mapping viết tay, không cần thư viện mới.

## 3. Theo dõi một lần đăng nhập

```text
Frontend lấy CSRF token
    ↓
POST /api/auth/login + cookie CSRF + header X-CSRF-TOKEN
    ↓
CsrfValidationFilter gọi IAntiforgery.ValidateRequestAsync
    ↓
AuthController → IAuthService → AuthService
    ↓
IAccountRepository → AccountRepository → AppDbContext → PostgreSQL
    ↓
Kiểm tra tài khoản, mật khẩu, thời gian khóa và hồ sơ còn hoạt động
    ↓
Tạo UserSession, lưu database, trả AuthSessionDto nội bộ
    ↓
API tạo ClaimsPrincipal và gọi HttpContext.SignInAsync
    ↓
ASP.NET Core tạo cookie đã được bảo vệ; response JSON chỉ có expiresAt và account
```

`Claim` là một thông tin về người đăng nhập, ví dụ AccountId hoặc Role. `ClaimsIdentity`
gom các claim theo cơ chế xác thực; `ClaimsPrincipal` là danh tính được gắn vào `HttpContext.User`.

Không tự mã hóa cookie hay tự hash mật khẩu. Cookie được Data Protection bảo vệ.
Mật khẩu dùng `PasswordHasher<UserAccount>`; không lưu mật khẩu gốc. Khi thuật toán/cấu hình
hash cần nâng cấp, `PasswordService` hỗ trợ rehash sau khi kiểm tra thành công.

Sai mật khẩu 5 lần sẽ khóa đăng nhập 15 phút. Bộ đếm sai được lưu trước khi service ném lỗi
401; nếu ném lỗi ngay trong transaction thì rollback sẽ làm mất bộ đếm vừa tăng.

## 4. Request sau đăng nhập diễn ra thế nào?

Trình duyệt tự gửi cookie. Cookie handler của ASP.NET Core kiểm tra tính toàn vẹn và hạn
của ticket. Sau đó `ClinicCookieEvents.ValidatePrincipal` gọi `AuthService.ValidateSessionAsync`:

- Phiên phải tồn tại, đúng tài khoản, chưa hết hạn.
- Tài khoản phải đang hoạt động, hồ sơ liên kết chưa bị xóa mềm.
- Quyền và liên kết sử dụng trong request được lấy từ database hiện tại.

Nếu không hợp lệ, server từ chối danh tính và xóa cookie. Endpoint cần đăng nhập sẽ trả
401 dạng JSON. Đã đăng nhập nhưng thiếu quyền thì trả 403 dạng JSON, không redirect.

`CurrentUser` đọc claim đã được kiểm tra, giúp service biết người gọi mà không phụ thuộc
vào ASP.NET Core. Client gửi `patientId` khác vẫn không thể vượt qua kiểm tra quyền sở hữu.

## 5. Vì sao dùng cookie rồi vẫn giữ UserSessions?

Cookie đã phát có thể còn hạn dù người dùng vừa đăng xuất. Chỉ xóa cookie ở trình duyệt
không vô hiệu hóa một bản cookie đã bị sao chép trước đó.

Giữ bản ghi phiên trong database cho phép thu hồi ngay ở request tiếp theo:

| Thao tác | Database | Trình duyệt hiện tại |
|---|---|---|
| Đăng xuất | Xóa phiên hiện tại | Xóa cookie đăng nhập |
| Đổi mật khẩu | Đổi hash và xóa mọi phiên trong cùng transaction | Xóa cookie đăng nhập |
| Admin đổi quyền/liên kết/trạng thái | Xóa mọi phiên của tài khoản đó | Cookie cũ bị từ chối khi gửi lại |

`UserSessionId` không phải Bearer token và không có endpoint cho client gửi ID để đăng nhập.
Nó chỉ được tin khi nằm trong ticket đã được ASP.NET Core xác thực. Database không còn TokenHash.

Đổi lại, mỗi request có cookie hợp lệ cần query phiên/tài khoản và kiểm tra hồ sơ liên kết.
Đây là lựa chọn có chủ đích để việc khóa tài khoản, đổi quyền, xóa hồ sơ có hiệu lực ngay.
Query phiên là `AsNoTracking()` và tìm theo khóa chính. Chưa thêm cache vì cache có thể trì hoãn
việc thu hồi quyền và làm luồng học phức tạp hơn.

Phiên có hạn cố định 8 giờ; không dùng sliding expiration để cookie và database thống nhất.
Cookie không có chế độ “ghi nhớ đăng nhập”. Khi người dùng đăng nhập thành công, service dọn
các phiên hết hạn của tài khoản đó. Chưa có job dọn phiên của các tài khoản không quay lại.

## 6. Cookie và CSRF phối hợp thế nào?

- `HttpOnly`: JavaScript không đọc được cookie đăng nhập.
- `Secure`: cookie chỉ được gửi qua HTTPS.
- `SameSite=Lax`: hạn chế gửi cookie trong ngữ cảnh khác site; vẫn cần kiểm tra CSRF.
- Tiền tố `__Host-`: yêu cầu Secure, Path=/ và không có Domain.
- `X-CSRF-TOKEN`: request token lấy từ `/api/auth/csrf`, gửi trên POST/PUT/PATCH/DELETE.

Endpoint CSRF đồng thời đặt cookie CSRF HttpOnly. JavaScript nhận **request token từ JSON**,
không cần đọc cookie. ASP.NET Core xác minh request token khớp cookie CSRF và danh tính hiện tại.
Vì vậy phải lấy lại token sau khi đăng nhập, đăng xuất, đổi mật khẩu hoặc đổi tài khoản ở tab khác.

`CsrfValidationFilter` chỉ là nơi gọi bộ kiểm tra có sẵn của ASP.NET Core cho các controller
JSON và multipart. Filter không tự tạo thuật toán chống CSRF, và không miễn trừ endpoint login.

Ví dụ frontend và cách thao tác Swagger nằm trong [tài liệu API](../api/ACCOUNTS_AUTH_API.md).

## 7. Chạy và kiểm tra

```powershell
dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api
dotnet run --project WebDatLichKhamBenh.Api --launch-profile https
```

Migration `UseCookieAuthentication` thu hồi phiên cũ và bỏ TokenHash; không xóa tài khoản,
hồ sơ hoặc lịch khám. Người dùng cần đăng nhập lại. Cần HTTPS development certificate
được máy/trình duyệt tin cậy. Frontend local nên dùng HTTPS và cùng hostname với API.

Test tích hợp đăng nhập qua HTTP thật, lưu cookie và lấy lại CSRF token sau login.
Không thay auth handler bằng giả lập, không tắt CSRF. Các test kiểm tra quyền sở hữu,
cookie bị sửa/hết hạn, cookie phát lại sau logout, đổi mật khẩu, đổi liên kết, và CORS.

Nghiệp vụ auth hiện dùng transaction chung sẵn có của dự án (`IClinicScheduleTransaction`).
Nó có khóa ghi chung để giữ các kiểm tra liên kết/Admin cuối cùng nhất quán. Cách này dễ
theo dõi ở quy mô hiện tại nhưng tuần tự hóa thao tác ghi; khi có tải cao cần đo và tách
khóa/transaction theo nghiệp vụ. Bản refactor này chưa thay cơ chế đồng bộ lịch khám.

## 8. Tự kiểm tra mình đã hiểu chưa

1. Vì sao không trả UserAccount trực tiếp ra API?
2. Vì sao controller tạo cookie nhưng service kiểm tra mật khẩu?
3. Sau login, tại sao token CSRF lấy trước đó không dùng cho logout được nữa?
4. Xóa cookie ở trình duyệt và xóa phiên trong database giải quyết hai việc gì?
5. Có role Patient rồi, tại sao vẫn phải kiểm tra patientId trong service?

Tài liệu nền: [Cookie Authentication của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-8.0)
và [chống CSRF của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0).
