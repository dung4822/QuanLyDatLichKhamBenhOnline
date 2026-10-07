using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WebDatLichKhamBenh.Api.Extensions;

namespace WebDatLichKhamBenh.Api.Security;

// Áp dụng cho mọi controller, kể cả đăng nhập/đăng ký và upload multipart/form-data.
public class CsrfValidationFilter : IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery;

    public CsrfValidationFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return;

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = context.HttpContext.CreateProblem(StatusCodes.Status400BadRequest,
                "CSRF token không hợp lệ. Gọi GET /api/auth/csrf rồi gửi requestToken trong header X-CSRF-TOKEN.");
            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        }
    }
}
