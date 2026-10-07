using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Services;

// Thực thi các "công cụ" chỉ đọc mà mô hình AI được phép gọi.
// Mỗi công cụ: đọc tham số → kiểm tra → gọi service sẵn có → trả JSON gọn cho mô hình.
public class ChatLookupService : IChatLookupService
{
    private readonly ISpecialtyService _specialtyService;
    private readonly IDoctorService _doctorService;
    private readonly IAppointmentSlotService _slotService;
    private readonly ILogger<ChatLookupService> _logger;

    public ChatLookupService(
        ISpecialtyService specialtyService,
        IDoctorService doctorService,
        IAppointmentSlotService slotService,
        ILogger<ChatLookupService> logger)
    {
        _specialtyService = specialtyService;
        _doctorService = doctorService;
        _slotService = slotService;
        _logger = logger;
    }

    private const string SpecialtiesTool = "get_specialties";
    private const string DoctorsTool = "get_doctors";
    private const string SlotsTool = "get_available_slots";

    private const int ResultLimit = 20;
    private const int QueryMaxLength = 100;

    // Tham số do mô hình sinh ra, nên đọc chặt: đúng camelCase, đúng kiểu, không có thuộc tính lạ.
    private static readonly JsonSerializerOptions ArgumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private record SpecialtySearch(string? Query);
    private record DoctorSearch(int? SpecialtyId, string? Query);
    private record SlotSearch(int? DoctorId, DateOnly? FromDate, DateOnly? ToDate);

    // Mô tả công cụ gửi cho mô hình (JSON Schema), giúp mô hình biết gọi công cụ nào, với tham số gì.
    public IReadOnlyList<LlmToolDefinition> Tools { get; } =
    [
        new(SpecialtiesTool,
            "Tra cứu chuyên khoa đang hoạt động theo tên. Tối đa 20 kết quả; nếu hasMore=true hãy tìm cụ thể hơn.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "maxLength": 100, "description": "Tên chuyên khoa, có thể không dấu." }
                  },
                  "additionalProperties": false
                }
                """)),
        new(DoctorsTool,
            "Tra cứu bác sĩ theo chuyên khoa hoặc tên. Chỉ trả dữ liệu công khai, tối đa 20 kết quả; nếu hasMore=true hãy tìm cụ thể hơn.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "specialtyId": { "type": "integer", "minimum": 1 },
                    "query": { "type": "string", "maxLength": 100, "description": "Tên bác sĩ hoặc chuyên khoa, có thể không dấu." }
                  },
                  "additionalProperties": false
                }
                """)),
        new(SlotsTool,
            "Tra cứu giờ trống của bác sĩ trong 7 ngày từ hôm nay theo giờ Việt Nam. doctorId phải lấy từ dữ liệu tra cứu. Công cụ chỉ đọc và không giữ chỗ. Tối đa 20 kết quả, có thể thu hẹp ngày khi hasMore=true.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "doctorId": { "type": "integer", "minimum": 1 },
                    "fromDate": { "type": "string", "format": "date", "description": "Ngày bắt đầu yyyy-MM-dd, mặc định hôm nay." },
                    "toDate": { "type": "string", "format": "date", "description": "Ngày kết thúc yyyy-MM-dd, mặc định hôm nay cộng 6 ngày." }
                  },
                  "required": ["doctorId"],
                  "additionalProperties": false
                }
                """))
    ];

    public async Task<ChatLookupResult> ExecuteAsync(LlmToolCall toolCall, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return toolCall.Name switch
            {
                SpecialtiesTool => await GetSpecialtiesAsync(ReadArguments<SpecialtySearch>(toolCall.Arguments)),
                DoctorsTool => await GetDoctorsAsync(ReadArguments<DoctorSearch>(toolCall.Arguments)),
                SlotsTool => await GetAvailableSlotsAsync(ReadArguments<SlotSearch>(toolCall.Arguments)),
                _ => Error(toolCall.Name, "unsupported_tool", "Công cụ này không được phép sử dụng.")
            };
        }
        catch (JsonException)
        {
            return InvalidArguments(toolCall.Name, "Tham số phải là JSON object đúng kiểu, không có thuộc tính lạ.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Lỗi DB... chỉ ghi log; mô hình chỉ nhận thông báo chung, không thấy chi tiết nội bộ.
            _logger.LogWarning(exception, "Không thể hoàn thành công cụ tra cứu chat {ToolName}.", toolCall.Name);
            return Error(toolCall.Name, "lookup_unavailable", "Chưa thể tra cứu dữ liệu lúc này. Vui lòng thử lại sau.");
        }
    }

    private async Task<ChatLookupResult> GetSpecialtiesAsync(SpecialtySearch search)
    {
        if (search.Query?.Length > QueryMaxLength)
            return InvalidArguments(SpecialtiesTool, $"query tối đa {QueryMaxLength} ký tự.");

        var query = NormalizeSearch(search.Query);
        var specialties = await _specialtyService.GetAllAsync();

        var (items, hasMore) = TakePage(specialties
            .Where(s => IsActive(s) && ContainsText(s.Name, query))
            .OrderBy(s => s.SpecialtyId)
            .Select(s => new { specialtyId = s.SpecialtyId, name = s.Name }));

        return Result(SpecialtiesTool, new { items, hasMore });
    }

    private async Task<ChatLookupResult> GetDoctorsAsync(DoctorSearch search)
    {
        if (search.SpecialtyId <= 0)
            return InvalidArguments(DoctorsTool, "specialtyId phải là số nguyên dương.");
        if (search.Query?.Length > QueryMaxLength)
            return InvalidArguments(DoctorsTool, $"query tối đa {QueryMaxLength} ký tự.");

        var query = NormalizeSearch(search.Query);
        // Chỉ giữ bác sĩ thuộc chuyên khoa đang hoạt động; lấy tên chuyên khoa mới nhất từ đây.
        var activeSpecialtyNames = (await _specialtyService.GetAllAsync())
            .Where(IsActive)
            .ToDictionary(s => s.SpecialtyId, s => s.Name);
        var doctors = await _doctorService.GetAllAsync(search.SpecialtyId);

        var (items, hasMore) = TakePage(doctors
            .Where(d => activeSpecialtyNames.ContainsKey(d.SpecialtyId))
            .Where(d => ContainsText(d.FullName, query) || ContainsText(activeSpecialtyNames[d.SpecialtyId], query))
            .OrderBy(d => d.DoctorId)
            // Chỉ chọn trường công khai; không đưa email, số điện thoại, địa chỉ cho mô hình.
            .Select(d => new
            {
                doctorId = d.DoctorId,
                fullName = d.FullName,
                specialtyId = d.SpecialtyId,
                specialtyName = activeSpecialtyNames[d.SpecialtyId],
                experience = d.Experience
            }));

        return Result(DoctorsTool, new { items, hasMore });
    }

    private async Task<ChatLookupResult> GetAvailableSlotsAsync(SlotSearch search)
    {
        if (search.DoctorId is null or <= 0)
            return InvalidArguments(SlotsTool, "doctorId là bắt buộc và phải là số nguyên dương.");

        var today = ClinicClock.Today;
        var lastDay = today.AddDays(ClinicSchedule.BookingWindowDays - 1);
        var from = search.FromDate ?? today;
        var to = search.ToDate ?? lastDay;
        if (from < today || to > lastDay || to < from)
            return InvalidArguments(SlotsTool,
                $"Khoảng ngày phải nằm từ {today:yyyy-MM-dd} đến {lastDay:yyyy-MM-dd}; ngày kết thúc không được trước ngày bắt đầu.");

        var doctor = await _doctorService.GetByIdAsync(search.DoctorId.Value);
        var specialty = doctor is null ? null : await _specialtyService.GetByIdAsync(doctor.SpecialtyId);
        if (doctor is null || specialty is null || !IsActive(specialty))
            return Error(SlotsTool, "doctor_unavailable", "Bác sĩ không tồn tại hoặc không còn nhận lịch khám.");

        // Service đã lọc sẵn: đúng bác sĩ, trong khoảng ngày, còn trống, chưa qua giờ, và đã sắp xếp theo thời gian.
        var slots = await _slotService.GetAvailableSlotsAsync(doctor.DoctorId, from, to) ?? [];
        var (items, hasMore) = TakePage(slots.Select(s => new
        {
            appointmentSlotId = s.AppointmentSlotId,
            date = s.Date,
            startTime = s.StartTime,
            endTime = s.EndTime
        }));

        return Result(SlotsTool, new
        {
            doctorId = doctor.DoctorId,
            doctorName = doctor.FullName,
            specialtyId = specialty.SpecialtyId,
            specialtyName = specialty.Name,
            fromDate = from,
            toDate = to,
            timeZone = "Asia/Ho_Chi_Minh",
            items,
            hasMore
        });
    }

    private static T ReadArguments<T>(string json)
        => JsonSerializer.Deserialize<T>(json, ArgumentOptions) ?? throw new JsonException("Tham số rỗng.");

    // Lấy dư 1 phần tử để biết còn kết quả phía sau hay không, mà không phải đếm toàn bộ.
    private static (List<T> Items, bool HasMore) TakePage<T>(IEnumerable<T> source)
    {
        var page = source.Take(ResultLimit + 1).ToList();
        return (page.Take(ResultLimit).ToList(), page.Count > ResultLimit);
    }

    private static bool IsActive(SpecialtyDto specialty) => SpecialtyStatus.IsActive(specialty.Status);

    private static bool ContainsText(string value, string? normalizedQuery)
        => normalizedQuery is null || NormalizeSearch(value)!.Contains(normalizedQuery, StringComparison.Ordinal);

    // Bỏ dấu tiếng Việt và chữ hoa để "dai thao duong" khớp với "Đái tháo đường".
    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // FormD tách "á" thành "a" + dấu sắc; sau đó bỏ phần dấu (NonSpacingMark).
        var result = new StringBuilder();
        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static ChatLookupResult Result(string tool, object data)
        => new(tool, JsonSerializer.SerializeToElement(data));

    private static ChatLookupResult Error(string tool, string code, string message)
        => Result(tool, new { error = new { code, message } });

    private static ChatLookupResult InvalidArguments(string tool, string message)
        => Error(tool, "invalid_arguments", message);
}
