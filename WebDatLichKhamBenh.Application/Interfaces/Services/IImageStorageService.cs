using WebDatLichKhamBenh.Application.DTOs.Images;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IImageStorageService
{
    Task<ImageUploadResult> UploadAsync(
        ImageUploadDto image,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
