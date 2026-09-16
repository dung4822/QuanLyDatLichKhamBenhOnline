using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.IntegrationTests;

public class AppointmentApiTests : IClassFixture<AppointmentApiFixture>
{
    private readonly AppointmentApiFixture _fixture;
    private HttpClient Client => _fixture.Client;
    public AppointmentApiTests(AppointmentApiFixture fixture) => _fixture = fixture;

    private Task<HttpResponseMessage> BookAsync(int patientId, int slotId, string? reason = null)
        => Client.PostAsJsonAsync("/api/appointments", new { patientId, appointmentSlotId = slotId, reason });

    private Task<HttpResponseMessage> RescheduleAsync(int id, int slotId)
        => Client.PatchAsJsonAsync($"/api/appointments/{id}/reschedule", new { appointmentSlotId = slotId });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reschedule_preserves_id_and_moves_booking_to_new_time_or_doctor(bool changeDoctor)
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        var target = changeDoctor ? s.OtherSlot : s.Slots[1];
        var response = await RescheduleAsync(id, target);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal(id, json.GetProperty("appointmentId").GetInt32());
        Assert.Equal(target, json.GetProperty("appointmentSlotId").GetInt32());
        Assert.Equal(changeDoctor ? s.OtherDoctorId : s.DoctorId, json.GetProperty("doctorId").GetInt32());
        Assert.Equal("Đau đầu", json.GetProperty("reason").GetString());
        Assert.Equal("Confirmed", json.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await RescheduleAsync(id, target)).StatusCode);
        await using var db = _fixture.CreateDbContext();
        var appointment = (await db.Appointments.FindAsync(id))!;
        var slot = await db.AppointmentSlots.Include(x => x.Doctor).ThenInclude(x => x.Specialty)
            .SingleAsync(x => x.AppointmentSlotId == target);
        Assert.Equal(slot.Date, appointment.Date);
        Assert.Equal(slot.StartTime, appointment.StartTime);
        Assert.Equal(slot.EndTime, appointment.EndTime);
        Assert.Equal(slot.Doctor.FullName, appointment.DoctorFullName);
        Assert.Equal(slot.Doctor.SpecialtyId, appointment.SpecialtyId);
        Assert.Equal(slot.Doctor.Specialty.Name, appointment.SpecialtyName);
        Assert.NotNull(appointment.UpdatedAt);
        Assert.Equal(AppointmentSlotStatus.Booked, slot.Status);
        Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        Assert.Equal(1, await db.Appointments.CountAsync(x => x.PatientId == s.PatientId));
    }

    [Theory]
    [InlineData(119, HttpStatusCode.Conflict)]
    [InlineData(121, HttpStatusCode.OK)]
    public async Task Reschedule_enforces_two_hour_deadline_on_current_appointment(int minutes, HttpStatusCode expected)
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        await using (var db = _fixture.CreateDbContext())
        {
            var appointment = (await db.Appointments.FindAsync(id))!;
            var start = ClinicClock.Now.AddMinutes(minutes);
            appointment.Date = DateOnly.FromDateTime(start);
            appointment.StartTime = TimeOnly.FromDateTime(start);
            appointment.EndTime = TimeOnly.FromDateTime(start.AddMinutes(30));
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, (await RescheduleAsync(id, s.Slots[1])).StatusCode);
    }

    [Fact]
    public async Task Reschedule_moves_to_another_day_and_keeps_invalid_old_slot_closed()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        int target;
        var date = s.Date.AddDays(1);
        await using (var db = _fixture.CreateDbContext())
        {
            var slot = new AppointmentSlot
            {
                DoctorId = s.DoctorId, Date = date,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30),
                DoctorShift = new DoctorShift
                {
                    DoctorId = s.DoctorId,
                    Shift = new Shift { Name = "Ca ngày khác", DayOfWeek = date.DayOfWeek,
                        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) }
                }
            };
            db.AppointmentSlots.Add(slot);
            // Dữ liệu nghỉ được import sau khi đặt: không được mở lại slot cũ.
            db.NonWorkingDays.Add(new NonWorkingDay { DoctorId = s.DoctorId, Date = s.Date });
            await db.SaveChangesAsync();
            target = slot.AppointmentSlotId;
        }
        Assert.Equal(HttpStatusCode.OK, (await RescheduleAsync(id, target)).StatusCode);
        await using var verify = _fixture.CreateDbContext();
        Assert.Equal(date, (await verify.Appointments.FindAsync(id))!.Date);
        Assert.Equal(AppointmentSlotStatus.Booked, (await verify.AppointmentSlots.FindAsync(target))!.Status);
        Assert.Equal(AppointmentSlotStatus.Unavailable, (await verify.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    [Theory]
    [InlineData("booked")]
    [InlineData("patient-overlap")]
    [InlineData("unavailable")]
    [InlineData("past")]
    [InlineData("outside-window")]
    [InlineData("save-failure")]
    public async Task Failed_reschedule_keeps_original_booking(string failure)
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s, s.Slots[1]);
        if (failure == "booked")
            Assert.Equal(HttpStatusCode.Created, (await BookAsync(s.OtherPatientId, s.OtherSlot)).StatusCode);
        if (failure == "patient-overlap")
            await BookSuccessfullyAsync(s);
        await using (var db = _fixture.CreateDbContext())
        {
            var slot = (await db.AppointmentSlots.FindAsync(s.OtherSlot))!;
            if (failure == "unavailable") slot.Status = AppointmentSlotStatus.Unavailable;
            if (failure == "past") slot.Date = ClinicClock.Today.AddDays(-1);
            if (failure == "outside-window") slot.Date = ClinicClock.Today.AddDays(7);
            await db.SaveChangesAsync();
        }
        if (failure == "save-failure") _fixture.Failures.FailNextAppointmentUpdate = true;
        Assert.Equal(failure == "save-failure" ? HttpStatusCode.InternalServerError : HttpStatusCode.Conflict,
            (await RescheduleAsync(id, s.OtherSlot)).StatusCode);
        await using var verify = _fixture.CreateDbContext();
        Assert.Equal(s.Slots[1], (await verify.Appointments.FindAsync(id))!.AppointmentSlotId);
        Assert.Equal(AppointmentSlotStatus.Booked, (await verify.AppointmentSlots.FindAsync(s.Slots[1]))!.Status);
        var expected = failure == "booked" ? AppointmentSlotStatus.Booked :
            failure == "unavailable" ? AppointmentSlotStatus.Unavailable : AppointmentSlotStatus.Available;
        Assert.Equal(expected, (await verify.AppointmentSlots.FindAsync(s.OtherSlot))!.Status);
    }

    [Fact]
    public async Task Reschedule_validates_ids_and_rejects_cancelled_appointment()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        Assert.Equal(HttpStatusCode.BadRequest, (await RescheduleAsync(id, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RescheduleAsync(int.MaxValue, s.Slots[1])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RescheduleAsync(id, int.MaxValue)).StatusCode);
        await Client.PatchAsJsonAsync($"/api/appointments/{id}/cancel", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await RescheduleAsync(id, s.Slots[1])).StatusCode);
    }

    [Fact]
    public async Task Concurrent_reschedule_and_booking_have_one_winner()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        var results = await Task.WhenAll(RescheduleAsync(id, s.Slots[1]), BookAsync(s.OtherPatientId, s.Slots[1]));
        Assert.Single(results.Where(x => x.IsSuccessStatusCode));
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.Appointments.CountAsync(x => x.AppointmentSlotId == s.Slots[1]));
        var moved = results[0].IsSuccessStatusCode;
        Assert.Equal(moved ? s.Slots[1] : s.Slots[0], (await db.Appointments.FindAsync(id))!.AppointmentSlotId);
        Assert.Equal(moved ? AppointmentSlotStatus.Available : AppointmentSlotStatus.Booked,
            (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<int> BookSuccessfullyAsync(Scenario s, int? slotId = null)
    {
        var response = await BookAsync(s.PatientId, slotId ?? s.Slots[0], "  Đau đầu  ");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("Confirmed", json.GetProperty("status").GetString());
        Assert.Equal("Đau đầu", json.GetProperty("reason").GetString());
        Assert.NotNull(response.Headers.Location);
        return json.GetProperty("appointmentId").GetInt32();
    }

    [Fact]
    public async Task Creates_reads_filters_and_pages_appointments()
    {
        var s = await _fixture.SeedAsync();
        var firstId = await BookSuccessfullyAsync(s);
        var secondId = await BookSuccessfullyAsync(s, s.Slots[1]);
        var url = $"/api/appointments?patientId={s.PatientId}&doctorId={s.DoctorId}&specialtyId={s.SpecialtyId}" +
            $"&fromDate={s.Date:yyyy-MM-dd}&toDate={s.Date:yyyy-MM-dd}&status=Confirmed&page=1&pageSize=1";
        var list = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var json = await JsonAsync(list);
        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(firstId, json.GetProperty("items")[0].GetProperty("appointmentId").GetInt32());
        var page2 = await JsonAsync(await Client.GetAsync(url.Replace("page=1", "page=2")));
        Assert.Equal(secondId, page2.GetProperty("items")[0].GetProperty("appointmentId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/appointments/{firstId}")).StatusCode);
        var empty = await JsonAsync(await Client.GetAsync($"/api/appointments?patientId={s.OtherPatientId}"));
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        var available = await JsonAsync(await Client.GetAsync($"/api/doctors/{s.DoctorId}/appointment-slots"));
        Assert.DoesNotContain(available.EnumerateArray(), x => x.GetProperty("appointmentSlotId").GetInt32() == s.Slots[0]);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?doctorId=-1")]
    [InlineData("?status=Unknown")]
    [InlineData("?status=99")]
    [InlineData("?fromDate=2026-09-12&toDate=2026-09-11")]
    public async Task Rejects_invalid_list_filters(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/appointments" + query)).StatusCode);

    [Fact]
    public async Task Validates_request_and_missing_resources()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(0, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(1, 1, new string('a', 1001))).StatusCode);
        var s = await _fixture.SeedAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await BookAsync(int.MaxValue, s.Slots[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BookAsync(s.PatientId, int.MaxValue)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/appointments/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PatchAsJsonAsync("/api/appointments/2147483647/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_booking_of_one_slot_has_exactly_one_winner()
    {
        var s = await _fixture.SeedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => BookAsync(i % 2 == 0 ? s.PatientId : s.OtherPatientId, s.Slots[0])));
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(11, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.Appointments.CountAsync(x => x.AppointmentSlotId == s.Slots[0]));
        Assert.Equal(AppointmentSlotStatus.Booked, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    [Fact]
    public async Task Concurrent_patient_bookings_with_different_doctors_cannot_overlap()
    {
        var s = await _fixture.SeedAsync();
        var results = await Task.WhenAll(BookAsync(s.PatientId, s.Slots[0]), BookAsync(s.PatientId, s.OtherSlot));
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Cancel_rebook_and_retry_old_cancellation_does_not_release_new_booking()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        var cancelUrl = $"/api/appointments/{id}/cancel";
        var cancelled = await Client.PatchAsJsonAsync(cancelUrl, new { reason = "  Bận  " });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Bận", (await JsonAsync(cancelled)).GetProperty("cancellationReason").GetString());
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(s.OtherPatientId, s.Slots[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PatchAsJsonAsync(cancelUrl, new { reason = "retry" })).StatusCode);
        await using var db = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Booked, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        Assert.Equal(2, await db.Appointments.CountAsync(x => x.AppointmentSlotId == s.Slots[0]));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PatchAsync($"/api/appointments/{id}/complete", null)).StatusCode);
    }

    [Fact]
    public async Task Appointment_insert_failure_rolls_back_slot_claim()
    {
        var s = await _fixture.SeedAsync();
        var response = await BookAsync(s.PatientId, s.Slots[0], "SIMULATE_SAVE_FAILURE");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Simulated", await response.Content.ReadAsStringAsync());
        await using var db = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        Assert.False(await db.Appointments.AnyAsync(x => x.AppointmentSlotId == s.Slots[0]));
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(s.PatientId, s.Slots[0])).StatusCode);
    }

    [Theory]
    [InlineData("past")]
    [InlineData("outside-window")]
    [InlineData("inactive-specialty")]
    [InlineData("deleted-doctor")]
    [InlineData("deleted-patient")]
    [InlineData("deleted-assignment")]
    [InlineData("inactive-shift")]
    [InlineData("shift-moved")]
    [InlineData("doctor-leave")]
    [InlineData("hospital-leave")]
    [InlineData("unavailable")]
    public async Task Rechecks_database_even_when_client_has_stale_slot(string change)
    {
        var s = await _fixture.SeedAsync();
        await using (var db = _fixture.CreateDbContext())
        {
            var slot = (await db.AppointmentSlots.FindAsync(s.Slots[0]))!;
            switch (change)
            {
                case "past": slot.Date = ClinicClock.Today.AddDays(-1); break;
                case "outside-window": slot.Date = ClinicClock.Today.AddDays(7); break;
                case "inactive-specialty": (await db.Specialties.FindAsync(s.SpecialtyId))!.Status = "Inactive"; break;
                case "deleted-doctor": (await db.Doctors.FindAsync(s.DoctorId))!.IsDelete = true; break;
                case "deleted-patient": (await db.Patients.FindAsync(s.PatientId))!.DeletedAt = DateTime.UtcNow; break;
                case "deleted-assignment": (await db.DoctorShifts.FindAsync(s.AssignmentId))!.IsDeleted = true; break;
                case "inactive-shift": (await db.Shifts.FindAsync(s.ShiftId))!.IsActive = false; break;
                case "shift-moved":
                    var shift = (await db.Shifts.FindAsync(s.ShiftId))!;
                    shift.StartTime = new TimeOnly(8, 15); shift.EndTime = new TimeOnly(10, 15);
                    break;
                case "doctor-leave": db.NonWorkingDays.Add(new NonWorkingDay { DoctorId = s.DoctorId, Date = s.Date }); break;
                case "hospital-leave": db.NonWorkingDays.Add(new NonWorkingDay { Date = s.Date }); break;
                case "unavailable": slot.Status = AppointmentSlotStatus.Unavailable; break;
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(change == "deleted-patient" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
            (await BookAsync(s.PatientId, s.Slots[0])).StatusCode);
        // Lịch nghỉ toàn viện là fixture bất thường, dọn ngay để không ảnh hưởng test khác.
        if (change == "hospital-leave")
        {
            await using var db = _fixture.CreateDbContext();
            await db.NonWorkingDays.Where(x => x.DoctorId == null && x.Date == s.Date).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Guards_schedule_leave_doctor_patient_and_specialty_changes()
    {
        var s = await _fixture.SeedAsync();
        await BookSuccessfullyAsync(s);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/doctors/{s.DoctorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/patients/{s.PatientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/specialties/{s.SpecialtyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/shifts/{s.ShiftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/doctors/{s.DoctorId}/shifts",
            new { shiftIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/api/non-working-days",
            new { date = s.Date, doctorId = s.DoctorId, reason = "Nghỉ" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/api/non-working-days",
            new { date = s.Date, reason = "Toàn viện nghỉ" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/shifts/{s.ShiftId}",
            new { dayOfWeek = s.Date.DayOfWeek, name = "Ca mới", startTime = "08:15", endTime = "10:15", isActive = true })).StatusCode);
        // Đổi tên ca không ảnh hưởng giờ khám.
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/shifts/{s.ShiftId}",
            new { dayOfWeek = s.Date.DayOfWeek, name = "Đổi tên", startTime = "08:00", endTime = "10:00", isActive = true })).StatusCode);
    }

    [Fact]
    public async Task Leave_and_slot_updates_roll_back_together_on_sync_failure()
    {
        var s = await _fixture.SeedAsync();
        _fixture.Failures.FailNextSlotSave = true;
        var response = await Client.PostAsJsonAsync("/api/non-working-days",
            new { date = s.Date, doctorId = s.DoctorId, reason = "Nghỉ" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = _fixture.CreateDbContext();
        Assert.False(await db.NonWorkingDays.AnyAsync(x => x.DoctorId == s.DoctorId && x.Date == s.Date));
        Assert.Equal(AppointmentSlotStatus.Available, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    [Fact]
    public async Task Cancel_does_not_reopen_slot_on_a_new_day_off()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        // Mô phỏng dữ liệu cũ/import trực tiếp; API thông thường đã chặn thao tác này.
        await using (var db = _fixture.CreateDbContext())
        {
            db.NonWorkingDays.Add(new NonWorkingDay { DoctorId = s.DoctorId, Date = s.Date });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PatchAsJsonAsync($"/api/appointments/{id}/cancel", new { })).StatusCode);
        await using var verify = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Unavailable, (await verify.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
    }

    [Theory]
    [InlineData("complete", "Completed")]
    [InlineData("no-show", "NoShow")]
    public async Task Final_status_requires_end_time_and_cannot_be_changed_again(string action, string status)
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        var url = $"/api/appointments/{id}/{action}";
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PatchAsync(url, null)).StatusCode);
        await using (var db = _fixture.CreateDbContext())
        {
            (await db.Appointments.FindAsync(id))!.Date = ClinicClock.Today.AddDays(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client.PatchAsJsonAsync($"/api/appointments/{id}/cancel", new { })).StatusCode);
        var completed = await Client.PatchAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(status, (await JsonAsync(completed)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Client.PatchAsync(url, null)).StatusCode);
        var otherAction = action == "complete" ? "no-show" : "complete";
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client.PatchAsync($"/api/appointments/{id}/{otherAction}", null)).StatusCode);
    }

    [Fact]
    public async Task History_survives_soft_deletion_and_profile_changes()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        var original = await JsonAsync(await Client.GetAsync($"/api/appointments/{id}"));
        await Client.PatchAsJsonAsync($"/api/appointments/{id}/cancel", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/doctors/{s.DoctorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/patients/{s.PatientId}")).StatusCode);
        await using (var db = _fixture.CreateDbContext())
        {
            var doctor = await db.Doctors.IgnoreQueryFilters().SingleAsync(x => x.DoctorId == s.DoctorId);
            doctor.FullName = "Tên đã thay đổi";
            await db.SaveChangesAsync();
        }
        var history = await JsonAsync(await Client.GetAsync($"/api/appointments/{id}"));
        Assert.Equal(original.GetProperty("doctorFullName").GetString(), history.GetProperty("doctorFullName").GetString());
        var list = await JsonAsync(await Client.GetAsync($"/api/appointments?patientId={s.PatientId}&status=Cancelled"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Reconciliation_preserves_booking_and_blocks_overlapping_legacy_slots()
    {
        var s = await _fixture.SeedAsync();
        await BookSuccessfullyAsync(s);
        await using (var db = _fixture.CreateDbContext())
        {
            var shift = (await db.Shifts.FindAsync(s.ShiftId))!;
            shift.StartTime = new TimeOnly(8, 15); shift.EndTime = new TimeOnly(10, 15);
            await db.SaveChangesAsync();
        }
        using (var scope = _fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAppointmentSlotService>().EnsureRollingWindowAsync();
        await using var verify = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Booked, (await verify.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        var overlapping = await verify.AppointmentSlots.SingleAsync(x => x.DoctorId == s.DoctorId &&
            x.Date == s.Date && x.StartTime == new TimeOnly(8, 15));
        Assert.Equal(AppointmentSlotStatus.Unavailable, overlapping.Status);
        // Mô phỏng stale Available: API vẫn kiểm tra chồng lịch ở bảng Appointments.
        overlapping.Status = AppointmentSlotStatus.Available;
        await verify.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await BookAsync(s.OtherPatientId, overlapping.AppointmentSlotId)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_leave_and_booking_never_both_succeed()
    {
        var s = await _fixture.SeedAsync();
        var bookingTask = BookAsync(s.PatientId, s.Slots[0]);
        var leaveTask = Client.PostAsJsonAsync("/api/non-working-days", new { date = s.Date, doctorId = s.DoctorId });
        var results = await Task.WhenAll(bookingTask, leaveTask);
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Single(results.Where(x => x.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Concurrent_reconciliation_does_not_overwrite_a_booking()
    {
        var s = await _fixture.SeedAsync();
        async Task SyncAsync()
        {
            using var scope = _fixture.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAppointmentSlotService>().EnsureRollingWindowAsync();
        }
        var booking = BookAsync(s.PatientId, s.Slots[0]);
        await Task.WhenAll(booking, SyncAsync(), SyncAsync());
        Assert.Equal(HttpStatusCode.Created, (await booking).StatusCode);
        await using var db = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Booked, (await db.AppointmentSlots.FindAsync(s.Slots[0]))!.Status);
        Assert.Equal(1, await db.Appointments.CountAsync(x => x.AppointmentSlotId == s.Slots[0]));
    }

    [Fact]
    public async Task Database_constraint_rejects_duplicate_non_cancelled_appointments()
    {
        var s = await _fixture.SeedAsync();
        var id = await BookSuccessfullyAsync(s);
        await using var db = _fixture.CreateDbContext();
        var duplicate = await db.Appointments.AsNoTracking().SingleAsync(x => x.AppointmentId == id);
        duplicate.AppointmentId = 0;
        duplicate.PatientId = s.OtherPatientId;
        db.Appointments.Add(duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<Npgsql.PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Equal("IX_Appointments_AppointmentSlotId", postgres.ConstraintName);
    }

    [Fact]
    public async Task Changing_doctor_specialty_is_guarded_and_resynchronizes_unbooked_slots()
    {
        var s = await _fixture.SeedAsync();
        await BookSuccessfullyAsync(s);
        int inactiveSpecialtyId;
        await using (var db = _fixture.CreateDbContext())
        {
            var specialty = new Specialty { Name = "Inactive " + Guid.NewGuid().ToString("N")[..10],
                Status = "Inactive", CreatedAt = DateTime.UtcNow };
            db.Specialties.Add(specialty);
            await db.SaveChangesAsync();
            inactiveSpecialtyId = specialty.SpecialtyId;
        }
        MultipartFormDataContent Form() => new()
        {
            { new StringContent("Bác sĩ cập nhật"), "FullName" },
            { new StringContent("0"), "Gender" },
            { new StringContent("2020-01-01"), "CareerStartDate" },
            { new StringContent(inactiveSpecialtyId.ToString()), "SpecialtyId" }
        };
        using var blockedForm = Form();
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client.PutAsync($"/api/doctors/{s.DoctorId}", blockedForm)).StatusCode);
        using var allowedForm = Form();
        var response = await Client.PutAsync($"/api/doctors/{s.OtherDoctorId}", allowedForm);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(inactiveSpecialtyId, (await JsonAsync(response)).GetProperty("specialtyId").GetInt32());
        await using var verify = _fixture.CreateDbContext();
        Assert.Equal(AppointmentSlotStatus.Unavailable, (await verify.AppointmentSlots.FindAsync(s.OtherSlot))!.Status);
    }
}
