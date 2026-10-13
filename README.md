# Hệ thống đặt lịch khám bệnh online (Backend)

Web API cho phòng khám: bệnh nhân tự đặt lịch khám theo khung giờ của bác sĩ; lễ tân, bác sĩ và admin quản lý lịch hẹn; bệnh nhân có thêm trợ lý AI hỗ trợ đặt, đổi, huỷ lịch qua chat.

- **Frontend:** [QuanLyDatLichKhamBenhOnline_Front](https://github.com/dung4822/QuanLyDatLichKhamBenhOnline_Front) (React + Vite)
- **Tech:** ASP.NET Core 8 Web API · PostgreSQL · EF Core · FluentValidation · Cookie Authentication · DeepSeek API · Cloudinary

---

## Dùng thử bản đã deploy

**Link:** https://datlichkhambenh.vercel.app

| Vai trò | Email | Mật khẩu |
|---|---|---|
| Admin | `admin@demo.com` | `MatKhauAdmin@2026` |
| Bệnh nhân | `benhnhan@demo.com` | `Demo@PhongKham2026` |
| Bác sĩ | `bacsi@demo.com` | `Demo@PhongKham2026` |
| Lễ tân | `letan@demo.com` | `Demo@PhongKham2026` |

Có thể tự đăng ký tài khoản bệnh nhân mới ở trang Đăng ký.

> **Lưu ý:** server dùng gói miễn phí nên tự "ngủ" khi không có người dùng.
> Lần mở đầu tiên có thể mất **30–60 giây**, sau đó chạy bình thường.

### Gợi ý kịch bản test
1. **Bệnh nhân:** xem lịch đã đặt → đặt lịch mới → đổi giờ → huỷ lịch → hỏi trợ lý AI ("đặt lịch khám da liễu ngày mai").
   Trợ lý AI **chỉ có ở tài khoản Bệnh nhân**, các vai trò khác không thấy mục chat.
2. **Lễ tân:** xem toàn bộ lịch hẹn → đặt lịch hộ bệnh nhân → đánh dấu đã khám / không đến.
3. **Bác sĩ:** xem lịch khám của riêng mình.

---

## Chức năng chính

- Quản lý chuyên khoa, bác sĩ (ảnh đại diện lưu trên Cloudinary), bệnh nhân
- Ca làm việc theo tuần, phân ca cho bác sĩ, ngày nghỉ của bác sĩ hoặc cả phòng khám
- Tự sinh khung giờ khám 30 phút trong 7 ngày tới từ lịch làm việc
- Đặt, đổi, huỷ lịch hẹn; ghi lịch sử thao tác cho từng lịch
- Phân quyền 4 vai trò: Admin, Lễ tân, Bác sĩ, Bệnh nhân
- Đăng nhập bằng cookie HttpOnly, chống CSRF, giới hạn số request (rate limit)
- Trợ lý AI (chỉ dành cho Bệnh nhân): chỉ **đề xuất** thao tác, bệnh nhân bấm xác nhận thì backend mới thực hiện

## Kiến trúc

```
WebDatLichKhamBenh.Api             Controller, middleware, cấu hình
WebDatLichKhamBenh.Application     Service nghiệp vụ, DTO, validator, interface
WebDatLichKhamBenh.Domain          Entity, enum
WebDatLichKhamBenh.Infrastructure  EF Core, repository, Cloudinary, DeepSeek
tests/                             Integration test
```

Luồng xử lý: `Controller → Service → Repository → DbContext → PostgreSQL`

## Chạy ở máy

### Bước 0: Cài đặt
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/download/) (nhớ mật khẩu user `postgres` đặt lúc cài)
- Công cụ EF Core: `dotnet tool install --global dotnet-ef --version 8.0.10`

Mọi lệnh bên dưới chạy trong thư mục gốc của repo (thư mục có file `WebDatLichKhamBenh.sln`).

### Bước 1: Cấu hình bí mật bằng user-secrets

**User-secrets** là nơi lưu mật khẩu và key trên máy bạn, nằm ngoài thư mục code nên không bao giờ bị commit lên Git.
App chỉ đọc user-secrets khi chạy ở môi trường Development (mặc định khi `dotnet run`).

**1.1. Chuỗi kết nối PostgreSQL** (bắt buộc). Thay `MAT_KHAU_POSTGRES` bằng mật khẩu user `postgres` của bạn:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=webdatlich;Username=postgres;Password=MAT_KHAU_POSTGRES" --project WebDatLichKhamBenh.Api
```

Database `webdatlich` chưa cần có sẵn, bước 2 sẽ tự tạo.

**1.2. DeepSeek API key** (tuỳ chọn, chỉ cần cho trợ lý AI):
1. Đăng ký tại https://platform.deepseek.com và nạp tiền (tính theo lượt dùng, vài đô dùng được lâu).
2. Vào **API keys → Create new API key**, copy key (dạng `sk-...`).
3. Chạy:

```bash
dotnet user-secrets set "DeepSeek:ApiKey" "sk-..." --project WebDatLichKhamBenh.Api
```

**1.3. Cloudinary** (tuỳ chọn, chỉ cần để upload ảnh đại diện bác sĩ):
1. Đăng ký miễn phí tại https://cloudinary.com.
2. Ở trang **Dashboard** (hoặc **Settings → API Keys**), copy **Cloud name**, **API Key** và **API Secret**.
3. Chạy lần lượt:

```bash
dotnet user-secrets set "Cloudinary:CloudName" "ten-cloud" --project WebDatLichKhamBenh.Api
```
```bash
dotnet user-secrets set "Cloudinary:ApiKey" "123456789012345" --project WebDatLichKhamBenh.Api
```
```bash
dotnet user-secrets set "Cloudinary:ApiSecret" "chuoi-api-secret" --project WebDatLichKhamBenh.Api
```

Bỏ qua 1.2 hoặc 1.3 thì app vẫn chạy được, chỉ có chat AI hoặc upload ảnh báo lỗi.

Kiểm tra đã lưu những gì (lệnh này in cả mật khẩu, đừng chụp gửi người khác):

```bash
dotnet user-secrets list --project WebDatLichKhamBenh.Api
```

### Bước 2: Tạo database và bảng

```bash
dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api
```

Cuối kết quả hiện `Done.` là thành công.

### Bước 3: Tạo tài khoản Admin

Hệ thống không có trang đăng ký Admin. Admin đầu tiên được tạo bằng lệnh, và **chỉ chạy được một lần** (khi chưa có Admin nào).
Chạy trong PowerShell, có thể đổi email và mật khẩu (mật khẩu ít nhất 12 ký tự):

```powershell
$env:BootstrapAdmin__Email = "admin@local.com"
$env:BootstrapAdmin__Password = "Admin@Local2026"
dotnet run --project WebDatLichKhamBenh.Api -- --bootstrap-admin
```

Thấy dòng `Đã khởi tạo Admin` là xong. Tài khoản Admin ở máy lúc này:

| Email | Mật khẩu |
|---|---|
| `admin@local.com` | `Admin@Local2026` |

Admin dùng để tạo tài khoản Lễ tân/Bác sĩ, quản lý chuyên khoa, bác sĩ, ca làm.

### Bước 4: Tạo dữ liệu demo (tuỳ chọn)

Tạo sẵn 5 chuyên khoa, 6 bác sĩ có ca làm, 5 bệnh nhân, 13 lịch hẹn và 3 tài khoản
`letan@demo.com`, `bacsi@demo.com`, `benhnhan@demo.com` dùng chung mật khẩu bạn đặt dưới đây.
Chỉ chạy được trên database chưa có chuyên khoa/bác sĩ nào.

```powershell
$env:DemoSeed__Password = "Demo@PhongKham2026"
dotnet run --project WebDatLichKhamBenh.Api -- --seed-demo
```

Xong bước 3–4, xoá các biến tạm để không dùng nhầm về sau:

```powershell
Remove-Item Env:BootstrapAdmin__Email, Env:BootstrapAdmin__Password, Env:DemoSeed__Password
```

### Bước 5: Chạy API

```bash
dotnet run --project WebDatLichKhamBenh.Api --launch-profile https
```

- Swagger (thử API trực tiếp): https://localhost:7023/swagger
- Chạy frontend: xem README của repo frontend. Frontend dev gọi `/api` và tự chuyển tiếp tới `https://localhost:7023`.

Chạy test: xem [tests/README.md](tests/README.md).

## Tài liệu

- API từng chức năng: [docs/api](docs/api)
- Hướng dẫn deploy miễn phí (Neon + Render + Vercel): [docs/deploy/DEPLOY_FREE.md](docs/deploy/DEPLOY_FREE.md)
- Ghi chú học tập: [docs/learning](docs/learning)
