using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Api.Extensions;
using WebDatLichKhamBenh.Application.Exceptions;

namespace WebDatLichKhamBenh.Api.Middlewares;

// Nơi DUY NHẤT đổi exception thành response lỗi. Controller và Service chỉ cần throw,
// không cần try/catch để tự dựng response.
public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã ngắt kết nối; không ghi thêm response.
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var status = GetStatusCode(exception);
            if (status >= 500)
                _logger.LogError(exception, "Request {Method} {Path} thất bại.", context.Request.Method, context.Request.Path);

            ProblemDetails problem = exception is ValidationException validation
                ? new ValidationProblemDetails(validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()))
                : new ProblemDetails();

            await context.WriteProblemAsync(context.Fill(problem, status, GetDetail(exception, status)));
        }
    }

    private static int GetStatusCode(Exception exception) => exception switch
    {
        ValidationException => StatusCodes.Status400BadRequest,
        AuthenticationRequiredException => StatusCodes.Status401Unauthorized,
        AccessDeniedException => StatusCodes.Status403Forbidden,
        ResourceNotFoundException => StatusCodes.Status404NotFound,
        AppointmentConflictException or AccountConflictException => StatusCodes.Status409Conflict,
        LlmUnavailableException => StatusCodes.Status502BadGateway,
        ImageStorageException => StatusCodes.Status503ServiceUnavailable,
        // Lỗi đọc request do Kestrel ném ra (body quá lớn, request hỏng...) đã mang sẵn status đúng.
        BadHttpRequestException badRequest => badRequest.StatusCode,
        _ => StatusCodes.Status500InternalServerError
    };

    // Message của các exception nghiệp vụ được viết sẵn để hiển thị cho người dùng.
    // Lỗi hệ thống và lỗi từ dịch vụ ngoài có thể chứa chi tiết nội bộ, nên dùng câu cố định.
    private static string? GetDetail(Exception exception, int status) => exception switch
    {
        ImageStorageException => "Dịch vụ lưu trữ ảnh đang không khả dụng. Dữ liệu chưa bị thay đổi.",
        BadHttpRequestException => null,
        _ when status == StatusCodes.Status500InternalServerError => "Đã xảy ra lỗi khi xử lý yêu cầu.",
        _ => exception.Message
    };
}
