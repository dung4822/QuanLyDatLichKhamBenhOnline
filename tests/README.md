# Chạy test trên máy local

Chạy các lệnh dưới đây tại thư mục chứa `WebDatLichKhamBenh.sln`, với .NET SDK 8.

## Test chat không cần database

```powershell
dotnet test tests/WebDatLichKhamBenh.IntegrationTests --filter "FullyQualifiedName~ChatApiTests|FullyQualifiedName~DeepSeekClientTests|FullyQualifiedName~ChatLookupServiceTests"
```

Các test này giả lập AI, kho lưu hội thoại và công cụ tra cứu. Không gọi DeepSeek thật và không tốn quota.
Chúng kiểm tra quyền truy cập, CSRF, dữ liệu đầu vào, lịch sử chat, gửi lại tin nhắn và xử lý lỗi AI.

## Chạy toàn bộ test với PostgreSQL local

Khởi động PostgreSQL trên máy rồi đặt chuỗi kết nối cho phiên PowerShell hiện tại:

```powershell
$env:APPOINTMENT_TEST_POSTGRES = 'Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=<mat-khau-local>;Pooling=false'
dotnet test WebDatLichKhamBenh.sln
```

Thay cổng, tài khoản và mật khẩu theo PostgreSQL local của bạn. Tài khoản cần quyền tạo database.
Nếu không đặt biến môi trường, test dùng `127.0.0.1:55439`, tài khoản `postgres`, không gửi mật khẩu.

Mỗi fixture tạo database riêng có tên `appointment_tests_<guid>`, áp dụng migration và tự xóa database đó khi kết thúc.
Không dùng chuỗi kết nối của server thật. Không lưu mật khẩu thật vào repository.

Nhóm test có database kiểm tra API tài khoản, phân quyền, danh mục, lịch hẹn và thao tác qua chat.
AI vẫn được giả lập. Có thể chạy riêng luồng đề xuất và xác nhận:

```powershell
dotnet test tests/WebDatLichKhamBenh.IntegrationTests --filter "FullyQualifiedName~ChatActionApiTests"
```

Luồng này kiểm tra việc đặt/đổi/hủy lịch chỉ thực hiện sau xác nhận, chống xác nhận trùng, quyền sở hữu,
thẻ hết hạn, hủy đề xuất, CSRF và rollback khi ghi database thất bại.
