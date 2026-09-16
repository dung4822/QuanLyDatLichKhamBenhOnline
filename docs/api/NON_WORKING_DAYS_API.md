# NON-WORKING DAYS API

`NonWorkingDay` lưu ngoại lệ theo ngày cụ thể, không phải lịch làm việc lặp lại hằng tuần.

| `doctorId` | Ý nghĩa |
| --- | --- |
| Có giá trị | Bác sĩ đó nghỉ trong ngày `date`. |
| `null` | Cả bệnh viện nghỉ trong ngày `date`; không bác sĩ nào có slot được chọn. |

## API

```http
GET    /api/non-working-days
GET    /api/non-working-days?fromDate=2026-08-20
GET    /api/non-working-days/{id}
POST   /api/non-working-days
PUT    /api/non-working-days/{id}
DELETE /api/non-working-days/{id}
```

Tạo ngày bác sĩ nghỉ:

```json
{
  "date": "2026-08-20",
  "doctorId": 5,
  "reason": "Nghỉ phép"
}
```

Tạo ngày cả bệnh viện nghỉ:

```json
{
  "date": "2026-09-02",
  "doctorId": null,
  "reason": "Quốc khánh"
}
```

`PUT` gửi đủ cả ba field `date`, `doctorId`, `reason`. `reason` là tùy chọn, dài tối đa 500 ký tự.

## Ràng buộc nghiệp vụ

- Chỉ tạo được cho ngày **sau hôm nay** theo giờ của server. Cùng ngày, ngày đã qua đều bị từ chối.
- Chỉ sửa hoặc hủy được một lịch nghỉ còn ở tương lai. Các ngày đã diễn ra được giữ lại làm lịch sử.
- `doctorId` phải là bác sĩ đang tồn tại (không bị soft delete); `null` có nghĩa chính xác là nghỉ toàn bệnh viện.
- Không thể có hai lịch nghỉ còn hiệu lực cùng phạm vi trong cùng ngày: một bác sĩ không thể nghỉ hai lần trong một ngày, và cả bệnh viện không thể có hai thông báo nghỉ trong một ngày. Database cũng có unique index để bảo vệ quy tắc này.
- Xóa là soft delete: `IsDeleted = true`, đồng thời cập nhật `DeletedAt` và `UpdatedAt`.

Một lịch nghỉ cá nhân có thể cùng tồn tại với lịch nghỉ toàn bệnh viện trong một ngày. Đây là dữ liệu dư thừa nhưng không làm sai nghiệp vụ: lịch nghỉ toàn bệnh viện luôn làm mọi bác sĩ không khả dụng. Khi cần, admin có thể xóa lịch cá nhân đó để giao diện gọn hơn.

## Dùng khi tạo AppointmentSlot

Khi sinh hoặc mở slot cho `doctorId` và `date`, `AppointmentSlotService` cần gọi:

```csharp
var isUnavailable = await _nonWorkingDayService.IsDoctorUnavailableAsync(doctorId, date);
```

Nếu `isUnavailable` là `true`, không tạo slot mới hoặc trả slot ở trạng thái không thể chọn. Hàm này kiểm tra cả hai trường hợp: bác sĩ nghỉ riêng và bệnh viện nghỉ chung.
