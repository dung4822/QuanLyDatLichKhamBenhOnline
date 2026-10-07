using FluentValidation;

namespace WebDatLichKhamBenh.Application.Validators;

public static class PagingRules
{
    public const int MaxPage = 1_000_000;
    public const int MaxPageSize = 100;

    public static void EnsureValid(int page, int pageSize)
    {
        if (page is < 1 or > MaxPage || pageSize is < 1 or > MaxPageSize)
            throw new ValidationException($"Page phải từ 1 đến {MaxPage}; PageSize từ 1 đến {MaxPageSize}.");
    }
}
