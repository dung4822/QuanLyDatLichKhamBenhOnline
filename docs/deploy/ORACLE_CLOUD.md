# Deploy toàn bộ hệ thống lên Oracle Cloud (Always Free) + tên miền DuckDNS

Kết quả: `https://ten-ban.duckdns.org` chạy cả frontend, API và PostgreSQL trên **một máy ảo miễn phí**.
Chi phí: 0đ (trừ tiền gọi DeepSeek). Thẻ Visa chỉ dùng để Oracle xác minh danh tính.

```text
Trình duyệt ──HTTPS──► Caddy (container web: FE tĩnh + chứng chỉ HTTPS)
                          ├── /api/*  ──► api:8080 (ASP.NET Core)  ──► db:5432 (PostgreSQL, volume pgdata)
                          └── còn lại ──► /srv (bản build Vite, SPA fallback về index.html)
```

FE và API chung một tên miền ⇒ cookie đăng nhập `SameSite=Lax` và CSRF chạy y như lúc dev, không cần CORS.

File liên quan:

| File | Vai trò |
|---|---|
| `Dockerfile` (repo backend) | Build API + bundle EF migration |
| `deploy/docker-compose.yml` | 4 service: `db`, `migrate` (chạy migration rồi thoát), `api`, `web` |
| `deploy/Caddyfile` | HTTPS tự động, chuyển `/api/*` sang API, phục vụ FE |
| `deploy/.env.example` | Mẫu biến môi trường (secret) |
| `Dockerfile` (repo frontend) | Build Vite rồi đóng gói vào image Caddy |

---

## 0. Chuẩn bị code (trên máy Windows)

1. Commit và push **cả hai repo** (server sẽ `git clone` về).
2. **Không commit secret.** `WebDatLichKhamBenh.Api/appsettings.json` trên máy đang chứa DeepSeek/Cloudinary key.
   Chuyển key sang User Secrets rồi để trống trong `appsettings.json` trước khi commit:

   ```powershell
   dotnet user-secrets set "DeepSeek:ApiKey" "KEY" --project WebDatLichKhamBenh.Api
   dotnet user-secrets set "Cloudinary:ApiSecret" "SECRET" --project WebDatLichKhamBenh.Api
   ```

   Trên server, mọi secret nằm trong `deploy/.env` (không commit, đã có trong `.gitignore`).

## 1. Tạo tài khoản và máy ảo Oracle

1. Đăng ký tại <https://www.oracle.com/cloud/free/>. **Home Region** không đổi được về sau — chọn
   Singapore hoặc Tokyo cho gần Việt Nam. Oracle có thể tạm giữ ~1 USD để xác minh thẻ rồi hoàn lại.
2. **Compute → Instances → Create instance**
   - Image: **Ubuntu 24.04** (hoặc 22.04).
   - Shape: **Ampere `VM.Standard.A1.Flex`**, 2 OCPU / 12 GB RAM (gói free cho tối đa 4 OCPU / 24 GB).
   - Networking: để mặc định, chọn *Assign a public IPv4 address*.
   - SSH keys: *Generate a key pair* → **tải private key về và giữ kỹ**.
   - Báo *Out of capacity*: thử lại sau vài giờ hoặc đổi Availability Domain. Phương án cuối là shape
     `VM.Standard.E2.1.Micro` (1 GB RAM — phải bật swap ở bước 3, build sẽ chậm).
3. Ghi lại **Public IP** của máy.
4. Mở cổng web: vào *Subnet → Security List → Add Ingress Rules*:
   Source `0.0.0.0/0`, TCP, Destination port **80** và **443** (thêm UDP 443 nếu muốn HTTP/3).

> Oracle có chính sách thu hồi máy Always Free bị "nhàn rỗi" lâu ngày. Nếu lo, có thể nâng tài khoản lên
> Pay As You Go (vẫn miễn phí trong hạn mức Always Free) và đặt **Budget alert** 1 USD để được báo nếu phát sinh phí.
> Chính sách có thể thay đổi — đọc lại trang Always Free của Oracle.

## 2. Tên miền miễn phí với DuckDNS

1. Vào <https://www.duckdns.org>, đăng nhập bằng GitHub/Google.
2. Tạo subdomain, VD `medicare-dung` → `medicare-dung.duckdns.org`.
3. Ô *current ip*: dán Public IP của máy Oracle → **update ip**.
4. Kiểm tra trên máy mình: `nslookup medicare-dung.duckdns.org` phải ra đúng IP.

IP công khai của Oracle giữ nguyên khi khởi động lại máy; chỉ đổi nếu xóa máy. Muốn chắc chắn, dùng
*Reserved Public IP* (miễn phí 1 cái).

## 3. Cài đặt trên server

SSH vào máy (PowerShell trên Windows):

```powershell
ssh -i C:\duong-dan\ssh-key.key ubuntu@<PUBLIC_IP>
```

Trên server:

```bash
# Ubuntu image của Oracle chặn sẵn mọi cổng trừ 22 bằng iptables: mở 80/443.
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p udp --dport 443 -j ACCEPT
sudo netfilter-persistent save

# Docker + Docker Compose
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
exit   # đăng nhập SSH lại để quyền docker có hiệu lực
```

Chỉ với máy 1 GB RAM (E2.1.Micro), bật swap 4 GB:

```bash
sudo fallocate -l 4G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## 4. Lấy code và cấu hình

Hai repo phải nằm **cạnh nhau**, repo frontend đặt tên thư mục `frontend`:

```bash
mkdir -p ~/app && cd ~/app
git clone https://github.com/dung4822/QuanLyDatLichKhamBenhOnline.git backend
git clone https://github.com/dung4822/QuanLyDatLichKhamBenhOnline_Front.git frontend
cd backend/deploy
cp .env.example .env
nano .env
```

Repo private: GitHub sẽ hỏi mật khẩu — dùng **Personal Access Token** (quyền đọc repo), không dùng mật khẩu GitHub.

Điền `.env`:

- `DOMAIN`: `medicare-dung.duckdns.org`
- `FRONTEND_DIR`: giữ `../../frontend`
- `POSTGRES_PASSWORD`: chuỗi dài ngẫu nhiên, chỉ chữ và số. Tạo nhanh: `openssl rand -hex 24`
- `DEEPSEEK_API_KEY`, `CLOUDINARY_*`: giá trị thật
- `BOOTSTRAP_ADMIN_*`: điền nếu DB mới tinh (bước 6)

## 5. Chạy

```bash
cd ~/app/backend/deploy
docker compose up -d --build      # lần đầu build trên ARM mất khoảng 5–15 phút
docker compose ps                 # db/api/web: running; migrate: exited (0)
docker compose logs -f web        # thấy "certificate obtained successfully" là có HTTPS
```

Mở `https://medicare-dung.duckdns.org`.

Mỗi lần `up`, service `migrate` tự áp dụng migration mới **trước khi** API chạy
(không còn lỗi 500 vì quên `dotnet ef database update`).

## 6. Dữ liệu ban đầu — chọn một trong hai

**A. DB mới, tạo Admin đầu tiên**

```bash
docker compose run --rm api --bootstrap-admin
```

Đăng nhập, đổi mật khẩu, rồi **xóa** `BOOTSTRAP_ADMIN_PASSWORD` trong `.env`.

**B. Chuyển dữ liệu đang có trên máy Windows lên**

```powershell
# Trên Windows (pg_dump nằm trong thư mục bin của PostgreSQL)
pg_dump -Fc -h localhost -U postgres -d <ten_db_local> -f clinic.dump
scp -i C:\duong-dan\ssh-key.key clinic.dump ubuntu@<PUBLIC_IP>:~/
```

```bash
# Trên server
cd ~/app/backend/deploy
docker compose stop api
docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner' < ~/clinic.dump
docker compose up -d
```

## 7. Cập nhật phiên bản mới

```bash
cd ~/app/frontend && git pull
cd ~/app/backend && git pull
cd deploy && docker compose up -d --build
docker image prune -f              # dọn image cũ
```

## 8. Sao lưu DB hằng ngày (nên làm)

```bash
mkdir -p ~/backups
crontab -e
```

Thêm dòng (2h sáng mỗi ngày, giữ 14 bản gần nhất):

```cron
0 2 * * * cd ~/app/backend/deploy && docker compose exec -T db sh -c 'pg_dump -Fc -U "$POSTGRES_USER" "$POSTGRES_DB"' > ~/backups/clinic-$(date +\%F).dump && ls -t ~/backups/*.dump | tail -n +15 | xargs -r rm
```

Thỉnh thoảng tải bản backup về máy mình (`scp`): nếu mất máy ảo thì mất cả backup trên đó.

## 9. Gặp lỗi

| Hiện tượng | Kiểm tra |
|---|---|
| Không mở được trang / không có HTTPS | DuckDNS đúng IP chưa; Security List và iptables đã mở 80/443 chưa; `docker compose logs web` |
| API 500 | `docker compose logs api`; migration lỗi: `docker compose logs migrate` |
| Chat trả 502 | `DEEPSEEK_API_KEY` sai/hết tiền; xem `docker compose logs api` |
| Mọi người bị đăng xuất sau mỗi lần deploy | Volume `dpkeys` bị xóa (đừng chạy `docker compose down -v`) |
| Mất sạch dữ liệu | `docker compose down -v` xóa volume `pgdata` — **không dùng `-v`**. Khôi phục từ backup (bước 6B) |
| Build bị kill (hết RAM) | Máy 1 GB: bật swap (bước 3) |

Xem DB nhanh: `docker compose exec db psql -U clinic -d clinic`.
