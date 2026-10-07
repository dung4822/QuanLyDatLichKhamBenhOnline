using FluentValidation;
using FluentValidation.Results;

namespace WebDatLichKhamBenh.Application.Exceptions;

public static class ValidationErrors
{
    // Lỗi 400 gắn với một field cụ thể; response sẽ có "errors": { "<field>": ["<message>"] }.
    public static ValidationException For(string propertyName, string message)
        => new(new[] { new ValidationFailure(propertyName, message) });
}
