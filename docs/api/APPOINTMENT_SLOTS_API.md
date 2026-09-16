# APPOINTMENT SLOTS — FRONTEND HANDOFF

Tài liệu đối chiếu với code backend ngày 11/09/2026. Phục vụ giao diện chọn bác sĩ, ngày và xem/chọn tạm khung giờ còn trống. Ví dụ là dữ liệu minh họa, không phải kết quả gọi API thực tế.

## Phạm vi hiện có

Chỉ có API GET danh sách slot khả dụng của một bác sĩ. Chưa có API giữ chỗ, đặt lịch, đổi trạng thái slot hoặc tạo Appointment. Việc click chọn giờ chỉ là state trên frontend, chưa lưu lịch khám và không bảo đảm giữ được chỗ.

Base URL local: `https://localhost:7023` hoặc `http://localhost:5244`, tùy profile đang chạy. Backend có HTTPS redirection. CORS cho phép HTTP/HTTPS localhost cổng 3000 và 5173. Endpoint hiện chưa yêu cầu xác thực.

## API contract

```http
GET /api/doctors/{doctorId}/appointment-slots
GET /api/doctors/{doctorId}/appointment-slots?fromDate=2026-09-11&toDate=2026-09-17
```

| Parameter | Vị trí | Kiểu | Quy tắc |
| --- | --- | --- | --- |
| `doctorId` | Path | Số nguyên | Dùng ID bác sĩ lấy từ `/api/doctors`; bác sĩ không tồn tại/đã xóa trả `404`. |
| `fromDate` | Query | `YYYY-MM-DD`, tùy chọn | Mặc định hôm nay theo giờ Việt Nam. |
| `toDate` | Query | `YYYY-MM-DD`, tùy chọn | Mặc định hôm nay + 6 ngày theo giờ Việt Nam. |

Hai đầu khoảng ngày đều được tính vào kết quả. Toàn bộ khoảng phải nằm trong cửa sổ **hôm nay đến hôm nay + 6 ngày**, và `fromDate <= toDate`. Đây là cửa sổ cố định theo hôm nay, không phải 7 ngày bắt đầu từ ngày tùy ý.

Ví dụ **nếu hôm nay ở Việt Nam là 11/09/2026**:

| Query | Khoảng được đọc / kết quả |
| --- | --- |
| Không gửi query | 11/09–17/09 |
| `fromDate=2026-09-13` | 13/09–17/09 |
| `toDate=2026-09-13` | 11/09–13/09 |
| `fromDate=2026-09-13&toDate=2026-09-13` | Chỉ ngày 13/09 |
| `fromDate=2026-09-10&toDate=2026-09-13` | `400`, có ngày quá khứ |
| `fromDate=2026-09-13&toDate=2026-09-18` | `400`, vượt cửa sổ |
| `fromDate=2026-09-14&toDate=2026-09-13` | `400`, khoảng đảo ngược |

Khi người dùng chọn một ngày, gửi **cả hai query bằng ngày đó**. Không hardcode các ngày minh họa vào UI.

## Response và kiểu frontend

Thành công trả `200` với array trực tiếp, không có wrapper `data` hay thông tin phân trang. Sắp xếp theo `date`, sau đó `startTime` tăng dần.

```ts
type AppointmentSlotStatus = "Available" | "Booked" | "Unavailable";

type AppointmentSlotDto = {
  appointmentSlotId: number;
  doctorId: number;
  doctorShiftId: number;
  date: string; // YYYY-MM-DD, ngày tại Việt Nam
  startTime: string; // HH:mm:ss, giờ tại Việt Nam
  endTime: string; // HH:mm:ss, giờ tại Việt Nam
  status: AppointmentSlotStatus;
  unavailableReason: string | null;
};
```

```json
[
  {
    "appointmentSlotId": 101,
    "doctorId": 5,
    "doctorShiftId": 12,
    "date": "2026-09-13",
    "startTime": "08:00:00",
    "endTime": "08:30:00",
    "status": "Available",
    "unavailableReason": null
  },
  {
    "appointmentSlotId": 102,
    "doctorId": 5,
    "doctorShiftId": 12,
    "date": "2026-09-13",
    "startTime": "08:30:00",
    "endTime": "09:00:00",
    "status": "Available",
    "unavailableReason": null
  }
]
```

Backend cấu hình `JsonStringEnumConverter`, vì vậy `status` là chuỗi, không phải số 0/1/2. Enum có ba trạng thái nhưng **endpoint này chỉ trả `Available`**; slot `Booked`/`Unavailable` bị lọc khỏi kết quả. `unavailableReason` có trong DTO, bình thường là `null` với slot khả dụng; endpoint này không cung cấp danh sách lý do của các slot bị ẩn.

`doctorShiftId` là mã phân công lịch tuần, không phải `shiftId`. Dùng `appointmentSlotId` làm key và định danh lựa chọn trên UI. DTO không có tên bác sĩ, chuyên khoa hoặc tên ca; thông tin bác sĩ lấy từ [DOCTORS_API.md](DOCTORS_API.md).

## Quy tắc ngày giờ và khả dụng

- Backend dùng `ClinicClock` với múi giờ Việt Nam (`Asia/Ho_Chi_Minh`, UTC+7).
- Slot được sinh dài 30 phút từ lịch tuần đang hiệu lực của bác sĩ và ca đang hoạt động. Ca không đủ 30 phút hoặc không chia hết cho 30 phút sẽ bị bỏ qua khi sinh slot.
- Trong hôm nay, chỉ trả slot có `startTime` **lớn hơn thời điểm hiện tại**; slot đã bắt đầu hoặc đang diễn ra không xuất hiện.
- Ngày bác sĩ nghỉ hoặc bệnh viện nghỉ khiến slot tương ứng thành không khả dụng khi đồng bộ. Nghỉ toàn bệnh viện được ưu tiên hơn nghỉ riêng.
- Sau đồng bộ, slot không còn thuộc lịch làm việc bị đánh dấu không khả dụng. Slot `Booked` được giữ nguyên bởi quá trình đồng bộ, nhưng không được GET này trả về.
- Frontend giữ `date`, `startTime`, `endTime` dưới dạng ngày/giờ tại Việt Nam; hiển thị `08:00–08:30`. Không tự gắn `Z` rồi đổi múi giờ thiết bị.

API GET chỉ đọc dữ liệu đã có, **không sinh slot theo yêu cầu**. Backend đồng bộ cửa sổ 7 ngày khi khởi động và vào nửa đêm Việt Nam; các luồng thay đổi lịch tuần, sửa/xóa ca, ngày nghỉ và xóa bác sĩ cũng gọi đồng bộ. Nếu đồng bộ lỗi hoặc chưa hoàn tất, dữ liệu đọc có thể chưa phản ánh cấu hình mới; nút tải lại chỉ đọc lại kết quả, không buộc chạy đồng bộ.

## Trạng thái rỗng và lỗi

| HTTP | Ý nghĩa / xử lý UI |
| --- | --- |
| `200` với `[]` | Bác sĩ tồn tại nhưng không có slot khả dụng trong khoảng. Hiện “Không có khung giờ khả dụng trong khoảng ngày đã chọn.” |
| `400` | Ngày sai định dạng hoặc khoảng ngày không hợp lệ. Hiện lỗi tại bộ chọn ngày. |
| `404` | Bác sĩ không tồn tại hoặc đã xóa. Xóa lựa chọn slot và yêu cầu chọn lại bác sĩ. |
| `500` hoặc lỗi mạng | Hiện lỗi tải lịch và nút thử lại; không trình bày như một ngày hết chỗ. |

Không suy ra từ `[]` rằng “bác sĩ nghỉ” hoặc “đã kín lịch”: có thể chưa phân ca, nghỉ, giờ đã qua, slot không khả dụng hoặc dữ liệu chưa được sinh. Không dựng các slot giả màu xám từ danh sách này để khẳng định giờ đã đặt.

Ví dụ phần dữ liệu lỗi khoảng ngày (framework có thể bổ sung `type`, `title`, `traceId`):

```json
{
  "status": 400,
  "errors": {
    "dateRange": ["Khoảng ngày phải nằm trong 7 ngày từ hôm nay theo giờ Việt Nam."]
  }
}
```

Lỗi binding có thể dùng key `fromDate` hoặc `toDate`, nên xử lý cả lỗi field và lỗi chung. `404` do bác sĩ không tồn tại có body:

```json
{ "message": "Doctor with ID 5 does not exist." }
```

Service kiểm tra bác sĩ trước khi validate khoảng ngày. Controller không chuẩn hóa riêng lỗi `500`; không giả định mọi lỗi đều có JSON hoặc `message`.

## Luồng giao diện cần tạo

1. Nạp danh sách bác sĩ từ `GET /api/doctors` và cho chọn bác sĩ.
2. Hiển thị 7 ngày từ hôm nay tại Việt Nam, hoặc date picker giới hạn cùng khoảng. Có thể tải toàn bộ cửa sổ rồi nhóm theo `date`.
3. Khi chọn bác sĩ/ngày, bỏ lựa chọn slot cũ, hiển thị loading và gọi GET. Hủy request cũ hoặc bỏ qua response cũ khi người dùng đổi lựa chọn nhanh.
4. Hiển thị các nút giờ từ response, dùng `appointmentSlotId` làm key; có trạng thái đang chọn.
5. Có trạng thái rỗng, lỗi, thử lại. Không cho chọn slot của kết quả cũ trong lúc đổi bác sĩ/ngày.
6. Nếu màn hình mở qua nửa đêm Việt Nam, tính lại giới hạn ngày và tải lại. Server quyết định khoảng hợp lệ khi đồng hồ client lệch.
7. Ghi rõ lựa chọn mới chỉ tạm thời. Chưa hiển thị thông báo “Đặt lịch thành công” hoặc nút xác nhận có tác dụng lưu khi chưa có API Appointment.

Ví dụ gọi cho một ngày (xử lý response lỗi và state loading trong lớp UI):

```ts
async function getSlotsForDate(
  baseUrl: string,
  doctorId: number,
  date: string,
  signal?: AbortSignal,
): Promise<AppointmentSlotDto[]> {
  const query = new URLSearchParams({ fromDate: date, toDate: date });
  const response = await fetch(
    `${baseUrl}/api/doctors/${doctorId}/appointment-slots?${query}`,
    { signal },
  );
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw { status: response.status, problem };
  }
  return response.json();
}
```

## Những API/chức năng chưa có

- GET chi tiết một slot hoặc GET tất cả trạng thái slot.
- POST/PUT/DELETE slot, API sinh slot thủ công.
- Giữ chỗ, đặt lịch, hủy/đổi lịch, chuyển slot sang `Booked` qua API.
- Lịch theo tháng hoặc ngoài cửa sổ 7 ngày.
- Query lọc chuyên khoa/trạng thái, phân trang cho endpoint slot.

Tài liệu liên quan: [lịch tuần và ca](SHIFT_SCHEDULE_API.md), [ngày nghỉ](NON_WORKING_DAYS_API.md). Phần cuối tài liệu lịch tuần hiện còn ghi NonWorkingDay/AppointmentSlot chưa có; nội dung đó đã cũ so với code và tài liệu này.

## Nguồn đối chiếu

- `WebDatLichKhamBenh.Api/Controllers/AppointmentSlotsController.cs`
- `WebDatLichKhamBenh.Application/Services/AppointmentSlotService.cs`
- `WebDatLichKhamBenh.Application/DTOs/AppointmentSlots/AppointmentSlotDto.cs`
- `WebDatLichKhamBenh.Domain/Enums/AppointmentSlotStatus.cs`
- `WebDatLichKhamBenh.Infrastructure/Repositories/AppointmentSlotRepository.cs`
- `WebDatLichKhamBenh.Application/Time/ClinicClock.cs`
- `WebDatLichKhamBenh.Api/Services/AppointmentSlotReconciliationService.cs`
- `WebDatLichKhamBenh.Api/Program.cs`, `Properties/launchSettings.json`
