using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WebDatLichKhamBenh.Application.Time;

namespace WebDatLichKhamBenh.IntegrationTests;

// Test "chốt hành vi" cho các API quản lý danh mục: refactor xong thì kết quả phải y như cũ.
public class CatalogApiTests : IClassFixture<AppointmentApiFixture>
{
    private readonly AppointmentApiFixture _fixture;
    private HttpClient Client => _fixture.Client;
    public CatalogApiTests(AppointmentApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Specialty_crud_keeps_status_codes()
    {
        var invalid = await Client.PostAsJsonAsync("/api/specialties", new { name = "", status = "Active" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Client.PostAsJsonAsync("/api/specialties", new { name = " Da liễu ", description = "Khám da", status = "Active" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var specialty = await JsonAsync(created);
        var id = specialty.GetProperty("specialtyId").GetInt32();
        Assert.Equal("Da liễu", specialty.GetProperty("name").GetString());

        using var anonymous = _fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/specialties")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/specialties/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/specialties/999999")).StatusCode);

        var updated = await Client.PutAsJsonAsync($"/api/specialties/{id}", new { name = "Da liễu 2", status = "Active" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Da liễu 2", (await JsonAsync(updated)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PutAsJsonAsync("/api/specialties/999999", new { name = "Không có", status = "Active" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/specialties/{id}")).StatusCode);
        var afterDelete = await JsonAsync(await Client.GetAsync($"/api/specialties/{id}"));
        Assert.Equal("Inactive", afterDelete.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/specialties/999999")).StatusCode);
    }

    [Fact]
    public async Task Shift_crud_keeps_status_codes()
    {
        var invalid = await Client.PostAsJsonAsync("/api/shifts", new
        {
            dayOfWeek = "Monday", name = "Sai giờ", startTime = "10:00:00", endTime = "09:00:00"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Client.PostAsJsonAsync("/api/shifts", new
        {
            dayOfWeek = "Monday", name = " Ca chiều ", startTime = "13:00:00", endTime = "15:00:00", isActive = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("shiftId").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/shifts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/shifts/999999")).StatusCode);

        var update = new { dayOfWeek = "Monday", name = "Ca chiều mới", startTime = "13:00:00", endTime = "16:00:00", isActive = true };
        var updated = await Client.PutAsJsonAsync($"/api/shifts/{id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Ca chiều mới", (await JsonAsync(updated)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("/api/shifts/999999", update)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/shifts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/shifts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/shifts/999999")).StatusCode);
    }

    [Fact]
    public async Task Doctor_crud_keeps_status_codes()
    {
        var s = await _fixture.SeedAsync();

        var invalid = await Client.PostAsync("/api/doctors", DoctorForm("Bác sĩ thiếu khoa", specialtyId: 999999));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Client.PostAsync("/api/doctors", DoctorForm(" Bác sĩ Mới ", s.SpecialtyId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var doctor = await JsonAsync(created);
        var id = doctor.GetProperty("doctorId").GetInt32();
        Assert.Equal("Bác sĩ Mới", doctor.GetProperty("fullName").GetString());
        Assert.Equal(DateTime.UtcNow.Year - 2020, doctor.GetProperty("experience").GetInt32());

        using var anonymous = _fixture.CreateClient();
        var list = await JsonAsync(await anonymous.GetAsync($"/api/doctors?specialtyId={s.SpecialtyId}"));
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("doctorId").GetInt32() == id);
        var detail = await anonymous.GetAsync($"/api/doctors/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal((await JsonAsync(detail)).GetProperty("specialtyName").GetString(),
            doctor.GetProperty("specialtyName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/doctors/999999")).StatusCode);

        var updated = await Client.PutAsync($"/api/doctors/{id}", DoctorForm("Bác sĩ Đổi Tên", s.SpecialtyId));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Bác sĩ Đổi Tên", (await JsonAsync(updated)).GetProperty("fullName").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PutAsync("/api/doctors/999999", DoctorForm("Không có", s.SpecialtyId))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/doctors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/doctors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/doctors/999999")).StatusCode);
    }

    [Fact]
    public async Task Doctor_with_upcoming_appointment_cannot_be_deleted()
    {
        var s = await _fixture.SeedAsync();
        var booked = await Client.PostAsJsonAsync("/api/appointments", new { patientId = s.PatientId, appointmentSlotId = s.Slots[0] });
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/doctors/{s.DoctorId}")).StatusCode);
    }

    [Fact]
    public async Task Doctor_weekly_schedule_keeps_status_codes()
    {
        var s = await _fixture.SeedAsync();
        var path = $"/api/doctors/{s.OtherDoctorId}/shifts";

        var schedule = await JsonAsync(await Client.GetAsync(path));
        Assert.Equal(s.ShiftId, Assert.Single(schedule.EnumerateArray()).GetProperty("shiftId").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/doctors/999999/shifts")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PutAsJsonAsync(path, new { shiftIds = new[] { 999999 } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PutAsJsonAsync("/api/doctors/999999/shifts", new { shiftIds = Array.Empty<int>() })).StatusCode);

        var cleared = await Client.PutAsJsonAsync(path, new { shiftIds = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await JsonAsync(cleared)).EnumerateArray());

        var restored = await Client.PutAsJsonAsync(path, new { shiftIds = new[] { s.ShiftId } });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Single((await JsonAsync(restored)).EnumerateArray());
    }

    [Fact]
    public async Task Non_working_day_crud_keeps_status_codes()
    {
        var s = await _fixture.SeedAsync();
        var date = ClinicClock.Today.AddDays(3);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync("/api/non-working-days", new { date = ClinicClock.Today, doctorId = s.OtherDoctorId })).StatusCode);

        var created = await Client.PostAsJsonAsync("/api/non-working-days", new { date, doctorId = s.OtherDoctorId, reason = " Nghỉ phép " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var day = await JsonAsync(created);
        var id = day.GetProperty("nonWorkingDayId").GetInt32();
        Assert.Equal("Nghỉ phép", day.GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync("/api/non-working-days", new { date, doctorId = s.OtherDoctorId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/non-working-days/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/non-working-days/999999")).StatusCode);

        var updated = await Client.PutAsJsonAsync($"/api/non-working-days/{id}", new { date = date.AddDays(1), doctorId = s.OtherDoctorId, reason = "   " });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(updated)).GetProperty("reason").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PutAsJsonAsync("/api/non-working-days/999999", new { date, doctorId = s.OtherDoctorId })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/non-working-days/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/non-working-days/{id}")).StatusCode);
    }

    [Fact]
    public async Task Patient_crud_keeps_status_codes()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.PostAsJsonAsync("/api/patients", new { fullName = "AB", dateOfBirth = "2000-01-01", phoneNumber = "0901" })).StatusCode);

        var created = await Client.PostAsJsonAsync("/api/patients", new
        {
            fullName = " Nguyễn Văn A ", dateOfBirth = "2000-01-01", gender = 0, phoneNumber = " 0901234567 ", email = "   ", address = " Hà Nội "
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = await JsonAsync(created);
        var id = patient.GetProperty("patientId").GetInt32();
        Assert.Equal("Nguyễn Văn A", patient.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, patient.GetProperty("email").ValueKind);
        Assert.Equal("Hà Nội", patient.GetProperty("address").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/patients/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/patients/999999")).StatusCode);

        var update = new { fullName = "Nguyễn Văn B", dateOfBirth = "2000-01-01", gender = 1, phoneNumber = "0901234567" };
        var updated = await Client.PutAsJsonAsync($"/api/patients/{id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Nguyễn Văn B", (await JsonAsync(updated)).GetProperty("fullName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("/api/patients/999999", update)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/patients/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/patients/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("/api/patients/999999")).StatusCode);
    }

    [Fact]
    public async Task Available_slots_keep_status_codes()
    {
        var s = await _fixture.SeedAsync();
        using var anonymous = _fixture.CreateClient();

        var slots = await anonymous.GetAsync($"/api/doctors/{s.DoctorId}/appointment-slots");
        Assert.Equal(HttpStatusCode.OK, slots.StatusCode);
        Assert.Equal(s.Slots.Count, (await JsonAsync(slots)).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/doctors/999999/appointment-slots")).StatusCode);
        var outsideWindow = ClinicClock.Today.AddDays(30).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.GetAsync($"/api/doctors/{s.DoctorId}/appointment-slots?toDate={outsideWindow}")).StatusCode);
    }

    private static MultipartFormDataContent DoctorForm(string fullName, int specialtyId) => new()
    {
        { new StringContent(fullName), "FullName" },
        { new StringContent("0", System.Text.Encoding.UTF8), "Gender" },
        { new StringContent("2020-01-01"), "CareerStartDate" },
        { new StringContent(specialtyId.ToString()), "SpecialtyId" }
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
