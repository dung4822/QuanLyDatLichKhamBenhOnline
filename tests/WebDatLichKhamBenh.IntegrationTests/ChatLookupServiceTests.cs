using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebDatLichKhamBenh.Application.DTOs.AppointmentSlots;
using WebDatLichKhamBenh.Application.DTOs.Chats;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.DTOs.Specialties;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.IntegrationTests;

public class ChatLookupServiceTests
{
    [Theory]
    [InlineData("create_appointment", "{}", "unsupported_tool")]
    [InlineData("get_specialties", "[]", "invalid_arguments")]
    [InlineData("get_specialties", "{", "invalid_arguments")]
    [InlineData("get_specialties", "{\"patientId\":1}", "invalid_arguments")]
    [InlineData("get_doctors", "{\"specialtyId\":0}", "invalid_arguments")]
    [InlineData("get_doctors", "{\"specialtyId\":1.5}", "invalid_arguments")]
    [InlineData("get_doctors", "{\"specialtyId\":\"1\"}", "invalid_arguments")]
    [InlineData("get_doctors", "{\"specialtyId\":2147483648}", "invalid_arguments")]
    [InlineData("get_available_slots", "{}", "invalid_arguments")]
    [InlineData("get_available_slots", "{\"doctorId\":1,\"fromDate\":\"03/10/2026\"}", "invalid_arguments")]
    public async Task InvalidOrUnsupportedToolCannotReachServices(string name, string arguments, string code)
    {
        var dependencies = new LookupDependencies();
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", name, arguments));

        Assert.Equal(code, result.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, dependencies.TotalReads);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(3000)]
    public async Task OversizedArgumentsAreRejectedBeforeQuerying(int length)
    {
        var dependencies = new LookupDependencies();
        var arguments = JsonSerializer.Serialize(new { query = new string('x', length) });
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_specialties", arguments));

        Assert.Equal("invalid_arguments", result.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, dependencies.TotalReads);
    }

    [Fact]
    public async Task SpecialtySearchIgnoresVietnameseAccentsAndInactiveEntries()
    {
        var dependencies = new LookupDependencies
        {
            Specialties =
            [
                new() { SpecialtyId = 1, Name = "Nội tiết Đái tháo đường", Status = "Active" },
                new() { SpecialtyId = 2, Name = "Đái tháo đường", Status = "Inactive" },
                new() { SpecialtyId = 3, Name = "Tim mạch", Status = "Active" }
            ]
        };
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_specialties", """{"query":"DAI THAO DUONG"}"""));

        var item = Assert.Single(result.Data.GetProperty("items").EnumerateArray());
        Assert.Equal(1, item.GetProperty("specialtyId").GetInt32());
        Assert.False(result.Data.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task DoctorSearchUsesActiveSpecialtiesAndExcludesPrivateFields()
    {
        var dependencies = new LookupDependencies
        {
            Specialties =
            [
                new() { SpecialtyId = 1, Name = "Tim mạch", Status = "Active" },
                new() { SpecialtyId = 2, Name = "Nội khoa", Status = "Inactive" }
            ],
            Doctors =
            [
                new() { DoctorId = 1, SpecialtyId = 1, FullName = "Đặng Ánh", SpecialtyName = "outdated name",
                    Email = "private@example.test", PhoneNumber = "private-phone", Address = "private-address", Experience = 5 },
                new() { DoctorId = 2, SpecialtyId = 2, FullName = "Đặng Ánh", SpecialtyName = "Nội khoa" }
            ]
        };
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_doctors", """{"query":"dang anh"}"""));

        var item = Assert.Single(result.Data.GetProperty("items").EnumerateArray());
        Assert.Equal(1, item.GetProperty("doctorId").GetInt32());
        Assert.Equal("Tim mạch", item.GetProperty("specialtyName").GetString());
        Assert.Equal(5, item.GetProperty("experience").GetInt32());
        Assert.Equal(new[] { "doctorId", "fullName", "specialtyId", "specialtyName", "experience" },
            item.EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("private", result.Data.GetRawText());
    }

    [Fact]
    public async Task ResultsAreCappedAndExposeOnlyPublicFields()
    {
        var dependencies = new LookupDependencies
        {
            Specialties = Enumerable.Range(1, 25).Reverse().Select(id => new SpecialtyDto
            {
                SpecialtyId = id, Name = $"Chuyên khoa {id}", Description = new string('x', 10000), Status = "Active"
            }).ToList()
        };
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_specialties", "{}"));

        Assert.Equal(20, result.Data.GetProperty("items").GetArrayLength());
        Assert.True(result.Data.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, result.Data.GetProperty("items")[0].GetProperty("specialtyId").GetInt32());
        Assert.All(result.Data.GetProperty("items").EnumerateArray(), item =>
            Assert.False(item.TryGetProperty("description", out _)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 7)]
    [InlineData(2, 1)]
    public async Task SlotRangeOutsideWindowIsRejectedBeforeQuerying(int fromOffset, int toOffset)
    {
        var dependencies = new LookupDependencies();
        var today = ClinicClock.Today;
        var arguments = JsonSerializer.Serialize(new { doctorId = 1, fromDate = today.AddDays(fromOffset), toDate = today.AddDays(toOffset) });
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_available_slots", arguments));

        Assert.Equal("invalid_arguments", result.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, dependencies.TotalReads);
    }

    [Fact]
    public async Task SlotLookupExcludesInactiveSpecialtyBeforeReadingSlots()
    {
        var dependencies = ValidDoctorDependencies();
        dependencies.Specialties[0].Status = "Inactive";
        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_available_slots", """{"doctorId":1}"""));

        Assert.Equal("doctor_unavailable", result.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, dependencies.SlotReads);
    }

    [Fact]
    public async Task SlotsAreCappedAndUseDefaultSevenDayWindow()
    {
        // Lọc slot trống/tương lai là việc của AppointmentSlotService; ở đây chỉ kiểm tra phần chat thêm vào.
        var dependencies = ValidDoctorDependencies();
        var today = ClinicClock.Today;
        dependencies.Slots = Enumerable.Range(1, 23).Select(id => new AppointmentSlotDto
        {
            AppointmentSlotId = id, DoctorId = 1, Date = today.AddDays(1),
            StartTime = new TimeOnly(7, 0).AddMinutes(id * 30),
            EndTime = new TimeOnly(7, 30).AddMinutes(id * 30), Status = AppointmentSlotStatus.Available
        }).ToList();

        var result = await CreateService(dependencies).ExecuteAsync(new("call-1", "get_available_slots", """{"doctorId":1}"""));

        Assert.Equal("Nguyễn An", result.Data.GetProperty("doctorName").GetString());
        Assert.Equal("Asia/Ho_Chi_Minh", result.Data.GetProperty("timeZone").GetString());
        Assert.Equal(Enumerable.Range(1, 20), result.Data.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("appointmentSlotId").GetInt32()));
        Assert.True(result.Data.GetProperty("hasMore").GetBoolean());
        Assert.Equal(today, dependencies.RequestedFromDate);
        Assert.Equal(today.AddDays(6), dependencies.RequestedToDate);
    }

    [Fact]
    public async Task LookupFailureReturnsSafeErrorAndCancellationPropagates()
    {
        var dependencies = new LookupDependencies { FailSpecialtyRead = true };
        var service = CreateService(dependencies);
        var result = await service.ExecuteAsync(new("call-1", "get_specialties", "{}"));

        Assert.Equal("lookup_unavailable", result.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("private-database-detail", result.Data.GetRawText());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(new("call-2", "get_specialties", "{}"), cancellation.Token));
        Assert.Equal(1, dependencies.TotalReads);
    }

    private static ChatLookupService CreateService(LookupDependencies dependencies)
        => new(dependencies, dependencies, dependencies, NullLogger<ChatLookupService>.Instance);

    private static LookupDependencies ValidDoctorDependencies() => new()
    {
        Specialties = [new() { SpecialtyId = 1, Name = "Tim mạch", Status = "Active" }],
        Doctors = [new() { DoctorId = 1, FullName = "Nguyễn An", SpecialtyId = 1, SpecialtyName = "Tim mạch" }]
    };

    private sealed class LookupDependencies : ISpecialtyService, IDoctorService, IAppointmentSlotService
    {
        public List<SpecialtyDto> Specialties { get; set; } = [];
        public List<DoctorDto> Doctors { get; set; } = [];
        public List<AppointmentSlotDto> Slots { get; set; } = [];
        public int TotalReads { get; private set; }
        public int SlotReads { get; private set; }
        public bool FailSpecialtyRead { get; set; }
        public DateOnly? RequestedFromDate { get; private set; }
        public DateOnly? RequestedToDate { get; private set; }

        Task<List<SpecialtyDto>> ISpecialtyService.GetAllAsync()
        {
            TotalReads++;
            if (FailSpecialtyRead) throw new InvalidOperationException("private-database-detail");
            return Task.FromResult(Specialties);
        }

        Task<SpecialtyDto?> ISpecialtyService.GetByIdAsync(int specialtyId)
        {
            TotalReads++;
            return Task.FromResult(Specialties.FirstOrDefault(x => x.SpecialtyId == specialtyId));
        }

        Task<List<DoctorDto>> IDoctorService.GetAllAsync(int? specialtyId)
        {
            TotalReads++;
            return Task.FromResult(Doctors);
        }

        Task<DoctorDto?> IDoctorService.GetByIdAsync(int doctorId)
        {
            TotalReads++;
            return Task.FromResult(Doctors.FirstOrDefault(x => x.DoctorId == doctorId));
        }

        public Task<List<AppointmentSlotDto>?> GetAvailableSlotsAsync(int doctorId, DateOnly? fromDate, DateOnly? toDate)
        {
            TotalReads++;
            SlotReads++;
            RequestedFromDate = fromDate;
            RequestedToDate = toDate;
            return Task.FromResult<List<AppointmentSlotDto>?>(Slots);
        }

        Task<SpecialtyDto> ISpecialtyService.CreateAsync(CreateSpecialtyRequest request) => throw new NotSupportedException();
        Task<SpecialtyDto?> ISpecialtyService.UpdateAsync(int id, UpdateSpecialtyRequest request) => throw new NotSupportedException();
        Task<bool> ISpecialtyService.DeleteAsync(int id) => throw new NotSupportedException();
        Task<DoctorDto> IDoctorService.CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<DoctorDto?> IDoctorService.UpdateAsync(int id, UpdateDoctorRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<bool> IDoctorService.DeleteAsync(int id) => throw new NotSupportedException();
        public Task EnsureRollingWindowAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SyncRangeAsync(DateOnly fromDate, DateOnly toDate, int? doctorId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
