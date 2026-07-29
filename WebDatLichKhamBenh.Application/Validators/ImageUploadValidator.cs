using FluentValidation;
using WebDatLichKhamBenh.Application.DTOs.Images;

namespace WebDatLichKhamBenh.Application.Validators;

public class ImageUploadValidator : AbstractValidator<ImageUploadDto>
{
    public const long MaximumFileSize = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    public ImageUploadValidator()
    {
        RuleFor(image => image)
            .Cascade(CascadeMode.Stop)
            .Must(image => image.Length > 0)
            .WithMessage("Ảnh không được rỗng.")
            .Must(image => image.Length <= MaximumFileSize)
            .WithMessage("Ảnh không được lớn hơn 5 MB.")
            .Must(image => AllowedContentTypes.Contains(image.ContentType))
            .WithMessage("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.")
            .Must(image => AllowedExtensions.Contains(Path.GetExtension(image.FileName)))
            .WithMessage("Phần mở rộng của ảnh phải là .jpg, .jpeg, .png hoặc .webp.")
            .MustAsync(HaveValidFileSignatureAsync)
            .WithMessage("Nội dung file không đúng định dạng JPEG, PNG hoặc WebP.");
    }

    private static async Task<bool> HaveValidFileSignatureAsync(
        ImageUploadDto image,
        CancellationToken cancellationToken)
    {
        if (!image.Content.CanSeek)
        {
            return false;
        }

        var originalPosition = image.Content.Position;
        var header = new byte[12];
        var bytesRead = 0;

        try
        {
            while (bytesRead < header.Length)
            {
                var read = await image.Content.ReadAsync(
                    header.AsMemory(bytesRead, header.Length - bytesRead),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }
        }
        finally
        {
            image.Content.Position = originalPosition;
        }

        return IsJpeg(header, bytesRead)
            || IsPng(header, bytesRead)
            || IsWebP(header, bytesRead);
    }

    private static bool IsJpeg(byte[] header, int bytesRead)
    {
        return bytesRead >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;
    }

    private static bool IsPng(byte[] header, int bytesRead)
    {
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return bytesRead >= pngSignature.Length
            && header.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature);
    }

    private static bool IsWebP(byte[] header, int bytesRead)
    {
        return bytesRead >= 12
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
    }
}
