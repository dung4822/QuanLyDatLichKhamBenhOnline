using Microsoft.AspNetCore.Mvc;

namespace WebDatLichKhamBenh.Api.Extensions;

// Chuẩn hóa response lỗi theo ProblemDetails (RFC 9457). Mọi lỗi đều có cùng hình dạng:
// { "type", "title", "status", "detail", "traceId" } và lỗi 400 có thêm "errors" theo từng field.
// - type: link mô tả loại lỗi (ở đây dùng link chuẩn của HTTP status code).
// - title: câu ngắn, cố định theo status code (frontend có thể hiển thị luôn).
// - detail: giải thích cụ thể cho lần lỗi này.
// - traceId: mã để tìm log khi người dùng báo lỗi.
public static class ProblemDetailsExtensions
{
    public static string TitleFor(int status) => status switch
    {
        400 => "Dữ liệu không hợp lệ.",
        401 => "Vui lòng đăng nhập.",
        403 => "Không có quyền truy cập.",
        404 => "Không tìm thấy dữ liệu.",
        405 => "Phương thức HTTP không được hỗ trợ.",
        409 => "Dữ liệu xung đột.",
        413 => "Dữ liệu gửi lên quá lớn.",
        415 => "Định dạng dữ liệu không được hỗ trợ.",
        429 => "Gửi quá nhiều yêu cầu.",
        502 => "Không thể nhận phản hồi từ trợ lý AI.",
        503 => "Dịch vụ bên ngoài tạm thời không khả dụng.",
        _ => "Không thể xử lý yêu cầu."
    };

    public static string TypeFor(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        405 => "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        413 => "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        415 => "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        429 => "https://tools.ietf.org/html/rfc6585#section-4",
        500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        502 => "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        503 => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        _ => "about:blank" // Giá trị mặc định theo RFC 9457 khi không có link riêng.
    };

    // Áp dụng cho ProblemDetails do ASP.NET Core tự tạo: NotFound(), ValidationProblem(...),
    // lỗi đọc JSON của [ApiController], và UseStatusCodePages (route không tồn tại, sai method...).
    public static IServiceCollection AddClinicProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            context.HttpContext.Fill(context.ProblemDetails, status, context.ProblemDetails.Detail);
        });
        return services;
    }

    // Tạo ProblemDetails cùng hình dạng cho những nơi nằm ngoài controller
    // (middleware, cookie authentication, rate limiter, filter).
    public static ProblemDetails CreateProblem(this HttpContext context, int status, string? detail = null)
        => context.Fill(new ProblemDetails(), status, detail);

    public static Task WriteProblemAsync(this HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        // Truyền kiểu thực tế (problem.GetType()): nếu là ValidationProblemDetails thì mới ghi ra "errors".
        return context.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null,
            contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }

    public static Task WriteProblemAsync(this HttpContext context, int status, string? detail = null)
        => context.WriteProblemAsync(context.CreateProblem(status, detail));

    // Điền các field chung. Mọi đường tạo lỗi đều đi qua hàm này nên hình dạng luôn giống nhau.
    public static TProblem Fill<TProblem>(this HttpContext context, TProblem problem, int status, string? detail)
        where TProblem : ProblemDetails
    {
        problem.Type = TypeFor(status);
        problem.Status = status;
        problem.Title = TitleFor(status);
        problem.Detail = detail;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }
}
