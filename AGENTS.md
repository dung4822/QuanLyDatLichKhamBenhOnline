# AGENTS.md

> Hướng dẫn AI hiểu project. Đọc file này trước khi tạo bất kỳ code nào.

---

## Project Overview

Dự án: **Hệ thống đặt lịch khám bệnh online**
Tech: ASP.NET Core Web API 8.0 · PostgreSQL · EF Core + LINQ · DTO · Mapper · DI

> Tên solution là `WebBanNongSan` (tên cũ), nhưng nghiệp vụ là **khám bệnh**.
> Luôn đặt tên theo khám bệnh: `Doctor`, `Patient`, `Appointment`, `AppointmentSlot`, `Specialty`.
> **Không đổi ngữ nghĩa sang bán nông sản.**

Định hướng: code đơn giản, làm chạy được trước, học đến đâu nâng cấp đến đó.

---

## Architecture

4 project theo Clean Architecture cơ bản:

```
WebBanNongSan.Api             ← Nhận request, trả response
WebBanNongSan.Application     ← Logic nghiệp vụ, interface, DTO
WebBanNongSan.Domain          ← Entity, Enum — không phụ thuộc layer nào
WebBanNongSan.Infrastructure  ← EF Core, Repository, DbContext
```

Dependency:

```
Api → Application → Domain
Infrastructure → Application → Domain
```

- `Application` không import `Infrastructure`.
- `Domain` không import gì cả.

---

## Project Aliases

| Nói tắt | Project |
|---|---|
| API / Api | `WebBanNongSan.Api` |
| Application / App | `WebBanNongSan.Application` |
| Domain | `WebBanNongSan.Domain` |
| Infrastructure / Infra | `WebBanNongSan.Infrastructure` |

> Không tạo folder trùng tên project ở root solution.

---

## Folder Structure

```
WebBanNongSan.Api
├── Controllers/
├── Extensions/
├── Middlewares/
└── Program.cs

WebBanNongSan.Application
├── DTOs/
├── Interfaces/
│   ├── Services/
│   └── Repositories/
├── Services/
└── Mappings/

WebBanNongSan.Domain
├── Entities/
└── Enums/

WebBanNongSan.Infrastructure
├── Persistence/
│   ├── AppDbContext.cs
│   └── Configurations/
├── Repositories/
└── DependencyInjection.cs
```

---

## Data Flow

```
Client → Controller → Service → Repository Interface → Repository Impl → DbContext → PostgreSQL
```

---

## Code Style

| Loại | Quy tắc | Ví dụ |
|---|---|---|
| Class, Method, Property | PascalCase | `DoctorService`, `GetAllAsync()` |
| Interface | Tiền tố `I` | `IDoctorService`, `IDoctorRepository` |
| Biến local, parameter | camelCase | `doctorId`, `createRequest` |
| Async method | Hậu tố `Async` | `GetByIdAsync()`, `CreateAsync()` |
| Controller | Số nhiều | `DoctorsController` |
| DTO | Rõ mục đích | `DoctorDto`, `CreateDoctorRequest` |
| Entity | Tên đơn, không hậu tố | `Doctor`, `Appointment` |

Route API — REST cơ bản:

```
GET    /api/doctors
GET    /api/doctors/{id}
POST   /api/doctors
PUT    /api/doctors/{id}
DELETE /api/doctors/{id}
```

---

## Common Patterns

### Controller — mỏng, chỉ gọi service

```csharp
[ApiController]
[Route("api/[controller]")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _doctorService;
    public DoctorsController(IDoctorService doctorService) => _doctorService = doctorService;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _doctorService.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _doctorService.GetByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }
}
```

### Service — xử lý nghiệp vụ

```csharp
public class DoctorService : IDoctorService
{
    private readonly IDoctorRepository _repo;
    public DoctorService(IDoctorRepository repo) => _repo = repo;

    public async Task<List<DoctorDto>> GetAllAsync()
    {
        var doctors = await _repo.GetAllAsync();
        return doctors.Select(d => new DoctorDto { Id = d.Id, FullName = d.FullName }).ToList();
    }
}
```

### Repository Interface — đặt ở Application

```csharp
public interface IDoctorRepository
{
    Task<List<Doctor>> GetAllAsync();
    Task<Doctor?> GetByIdAsync(Guid id);
    Task AddAsync(Doctor doctor);
    Task SaveChangesAsync();
}
```

### Repository Implementation — đặt ở Infrastructure

```csharp
public class DoctorRepository : IDoctorRepository
{
    private readonly AppDbContext _context;
    public DoctorRepository(AppDbContext context) => _context = context;

    public async Task<List<Doctor>> GetAllAsync()
        => await _context.Doctors.AsNoTracking().ToListAsync();

    public async Task<Doctor?> GetByIdAsync(Guid id)
        => await _context.Doctors.FirstOrDefaultAsync(x => x.Id == id);

    public async Task AddAsync(Doctor doctor) => await _context.Doctors.AddAsync(doctor);
    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
```

---

## Do's & Don'ts

**Nên:**
- Tách rõ Controller → Service → Repository, đúng layer.
- Dùng DTO cho dữ liệu vào/ra API, Entity cho database.
- Dùng interface để DI, `async/await` cho database, `AsNoTracking()` cho query chỉ đọc.
- Thêm chức năng theo luồng: Entity → Repository → Service → Controller.

**Không nên:**
- Viết logic trong Controller, hay để API gọi thẳng DbContext.
- Trả Entity ra ngoài API, hoặc để Application import Infrastructure.
- Đặt tên mơ hồ: `Manager`, `Helper`, `Data`, `Model1`.
- Thêm kiến trúc phức tạp (CQRS, MediatR) khi chưa cần.

---

## Notes For Future

Sau này khi cần: FluentValidation · AutoMapper · Global exception middleware · JWT Auth · Pagination · Logging · Unit tests.

Hiện tại chưa cần. **Học đến đâu, thêm đến đó.**
