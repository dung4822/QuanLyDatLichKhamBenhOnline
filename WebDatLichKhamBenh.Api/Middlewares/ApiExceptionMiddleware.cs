using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using WebDatLichKhamBenh.Application.Exceptions;

namespace WebDatLichKhamBenh.Api.Middlewares;

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
            var status = exception switch
            {
                ValidationException => StatusCodes.Status400BadRequest,
                ResourceNotFoundException => StatusCodes.Status404NotFound,
                AppointmentConflictException => StatusCodes.Status409Conflict,
                AccountConflictException => StatusCodes.Status409Conflict,
                AuthenticationRequiredException => StatusCodes.Status401Unauthorized,
                AccessDeniedException => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };
            if (status == 500)
                _logger.LogError(exception, "Request {Method} {Path} thất bại.", context.Request.Method, context.Request.Path);

            ProblemDetails problem = exception is ValidationException validation
                ? new ValidationProblemDetails(validation.Errors.GroupBy(x => x.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(x => x.ErrorMessage).ToArray()))
                : new ProblemDetails();
            problem.Status = status;
            problem.Title = status switch
            {
                400 => "Dữ liệu không hợp lệ.",
                401 => "Vui lòng đăng nhập.",
                403 => "Không có quyền truy cập.",
                404 => "Không tìm thấy dữ liệu.",
                409 => "Dữ liệu xung đột.",
                _ => "Không thể xử lý yêu cầu."
            };
            problem.Detail = status == 500 ? "Đã xảy ra lỗi khi xử lý yêu cầu." : exception.Message;
            problem.Extensions["traceId"] = context.TraceIdentifier;
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
