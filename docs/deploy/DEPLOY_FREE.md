# Deploy miễn phí: Neon + Render + Vercel

Mục tiêu: có một đường link cho nhà tuyển dụng vào test, chi phí 0đ, không cần thẻ.

```
Trình duyệt ──► Vercel (frontend, xxx.vercel.app)
                  │  /api/*  (Vercel chuyển tiếp, trình duyệt không biết)
                  ▼
                Render (API .NET chạy Docker) ──► Neon (PostgreSQL)
```

**Vì sao phải chuyển tiếp `/api` qua Vercel?** Cookie đăng nhập dùng `SameSite=Lax` và tiền tố `__Host-`.
Nếu frontend (`vercel.app`) gọi thẳng sang `onrender.com` thì đó là gọi *khác site*, nên trình duyệt không gửi cookie.
Khi đi qua Vercel, trình duyệt chỉ thấy một domain, giống hệt proxy của Vite lúc dev.

Các file liên quan:

| File | Vai trò |
|---|---|
| `Dockerfile`, `.dockerignore` | Render build API thành image |
| `render.yaml` | Khai báo service trên Render (Blueprint) |
| `Program.cs` → `/health` | Render và dịch vụ ping kiểm tra app còn sống |
| `vercel.json` (repo frontend) | Chuyển tiếp `/api/*` sang Render + trả `index.html` cho mọi trang |

---

## Bước 1: Neon (database)

1. Đăng ký tại https://neon.tech (đăng nhập bằng GitHub), tạo project, chọn region **Singapore**.
2. Vào **Connect**, **tắt** "Connection pooling", rồi copy thông tin kết nối.
3. Neon đưa dạng `postgresql://user:pass@host/neondb?sslmode=require`, còn Npgsql cần dạng key=value:

   ```
   Host=ep-xxx.ap-southeast-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require
   ```

   Lưu chuỗi này vào chỗ an toàn (không commit). Bên dưới gọi nó là `NEON_CONN`.

4. Tạo bảng (chạy trên máy bạn, trong thư mục `D:\WebBanNongSan`):

   ```bash
   dotnet ef database update --project WebDatLichKhamBenh.Infrastructure --startup-project WebDatLichKhamBenh.Api --connection "NEON_CONN"
   ```

5. Tạo tài khoản Admin đầu tiên (PowerShell):

   ```powershell
   $env:ConnectionStrings__DefaultConnection = "NEON_CONN"
   $env:BootstrapAdmin__Email = "admin@demo.com"
   $env:BootstrapAdmin__Password = "mật-khẩu-mạnh-của-bạn"
   dotnet run --project WebDatLichKhamBenh.Api -- --bootstrap-admin
   ```

   Mở terminal mới sau khi chạy xong để các biến môi trường trên không còn tác dụng.

6. (Tuỳ chọn) Tạo dữ liệu demo: 5 chuyên khoa, 6 bác sĩ có ca làm, 5 bệnh nhân, lịch hẹn đủ trạng thái
   và 3 tài khoản `letan@demo.com` (Lễ tân), `bacsi@demo.com` (Bác sĩ), `benhnhan@demo.com` (Bệnh nhân)
   dùng chung một mật khẩu (ít nhất 12 ký tự). Chỉ chạy được trên database chưa có chuyên khoa/bác sĩ nào.

   ```powershell
   $env:ConnectionStrings__DefaultConnection = "NEON_CONN"
   $env:DemoSeed__Password = "mật-khẩu-demo-ít-nhất-12-ký-tự"
   dotnet run --project WebDatLichKhamBenh.Api -- --seed-demo
   ```

> Mỗi khi thêm migration mới, chạy lại lệnh ở mục 4.

## Bước 2: Render (backend)

1. Push code backend lên GitHub.
2. Đăng ký https://render.com bằng GitHub → **New → Blueprint** → chọn repo `QuanLyDatLichKhamBenhOnline`.
   Render đọc `render.yaml` và hỏi các biến bí mật:

   | Biến | Giá trị |
   |---|---|
   | `ConnectionStrings__DefaultConnection` | `NEON_CONN` |
   | `DeepSeek__ApiKey` | key DeepSeek (để trống thì chat AI sẽ báo lỗi) |
   | `Cloudinary__CloudName` / `ApiKey` / `ApiSecret` | thông tin Cloudinary |

   Dấu `__` (hai gạch dưới) thay cho dấu `:` trong `appsettings.json`.

3. Chờ build xong (lần đầu khoảng 5–10 phút). Mở `https://<tên-service>.onrender.com/health`, thấy `"ok"` là được.
4. **Kiểm tra lại URL thật** Render cấp. Nếu khác `webdatlichkhambenh-api.onrender.com` thì sửa trong `vercel.json` của repo frontend.

## Bước 3: Vercel (frontend)

1. Push repo frontend (có `vercel.json`) lên GitHub.
2. Đăng ký https://vercel.com bằng GitHub → **Add New → Project** → chọn `QuanLyDatLichKhamBenhOnline_Front`.
   Vercel tự nhận ra Vite. **Không** đặt `VITE_API_BASE_URL`, để mặc định là `/api`.
3. Deploy, mở link `xxx.vercel.app`, rồi đăng nhập bằng tài khoản Admin.

## Bước 4: Giữ app không ngủ (tuỳ chọn)

Render free tắt app nếu 15 phút không có request, lần mở sau phải chờ 30–60 giây.
Tạo job miễn phí tại https://cron-job.org gọi `https://<tên-service>.onrender.com/health` mỗi 10 phút.
Một service chạy 24/7 vẫn nằm trong 750 giờ free mỗi tháng.

## Bước 5: Chuẩn bị cho nhà tuyển dụng

- Đăng nhập Admin, tạo sẵn chuyên khoa, bác sĩ, ca làm và vài lịch khám mẫu.
- Tạo tài khoản demo cho từng vai trò. Ghi email và mật khẩu demo vào README hoặc CV
  (chỉ dùng mật khẩu riêng cho demo, không dùng lại mật khẩu thật).
- DeepSeek tính tiền theo lượt dùng: đặt giới hạn chi tiêu, API đã giới hạn 10 tin nhắn/phút cho mỗi người.

## Giới hạn cần biết

- **Bị đăng xuất sau khi Render khởi động lại.** Khoá mã hoá cookie (Data Protection) nằm trong container và mất khi
  container được tạo lại (deploy mới hoặc sau khi app ngủ dậy). Với bản demo thì chấp nhận được: chỉ cần đăng nhập lại.
- Neon free: 0.5 GB, DB tự ngủ khi rảnh, lần truy vấn đầu chậm khoảng 1 giây.
- Rate limit theo IP: mọi request đi qua Vercel nên có thể bị tính chung IP. Nếu test nhiều mà gặp lỗi 429,
  tăng `Authentication__RequestsPerMinute` trên Render.
