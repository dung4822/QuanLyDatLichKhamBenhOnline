using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using WebDatLichKhamBenh.Application.DTOs.Images;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using StorageImageUploadResult =
    WebDatLichKhamBenh.Application.DTOs.Images.ImageUploadResult;

namespace WebDatLichKhamBenh.Infrastructure.Storage;

public class CloudinaryImageStorageService : IImageStorageService
{
    private readonly CloudinarySettings _settings;

    public CloudinaryImageStorageService(CloudinarySettings settings)
    {
        _settings = settings;
    }

    public async Task<StorageImageUploadResult> UploadAsync(
        ImageUploadRequest image,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cloudinary = CreateCloudinaryClient();

            if (image.Content.CanSeek)
            {
                image.Content.Position = 0;
            }

            var uploadParameters = new ImageUploadParams
            {
                File = new FileDescription(image.FileName, image.Content),
                PublicId = $"doctors/{Guid.NewGuid():N}",
                Overwrite = false
            };

            var result = await cloudinary.UploadAsync(uploadParameters, cancellationToken);

            if (result.Error is not null)
            {
                throw new ImageStorageException(
                    $"Cloudinary từ chối upload: {result.Error.Message}");
            }

            var secureUrl = result.SecureUrl?.AbsoluteUri;

            if (string.IsNullOrWhiteSpace(secureUrl)
                || string.IsNullOrWhiteSpace(result.PublicId))
            {
                throw new ImageStorageException(
                    "Cloudinary không trả về URL hoặc public ID của ảnh.");
            }

            return new StorageImageUploadResult(secureUrl, result.PublicId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImageStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ImageStorageException(
                "Không thể upload ảnh lên Cloudinary.",
                exception);
        }
    }

    public async Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cloudinary = CreateCloudinaryClient();
            var deleteParameters = new DeletionParams(storageKey)
            {
                ResourceType = ResourceType.Image,
                Invalidate = true
            };

            cancellationToken.ThrowIfCancellationRequested();
            var result = await cloudinary.DestroyAsync(deleteParameters);

            if (result.Error is not null)
            {
                throw new ImageStorageException(
                    $"Cloudinary từ chối xóa ảnh: {result.Error.Message}");
            }

            var deleteResult = result.Result;
            if (!string.Equals(deleteResult, "ok", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(deleteResult, "not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new ImageStorageException(
                    $"Cloudinary trả về trạng thái xóa không hợp lệ: {deleteResult}.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImageStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ImageStorageException(
                "Không thể xóa ảnh trên Cloudinary.",
                exception);
        }
    }

    private Cloudinary CreateCloudinaryClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.CloudName)
            || string.IsNullOrWhiteSpace(_settings.ApiKey)
            || string.IsNullOrWhiteSpace(_settings.ApiSecret))
        {
            throw new ImageStorageException(
                "Cloudinary chưa được cấu hình. Hãy cấu hình CloudName, ApiKey và ApiSecret.");
        }

        var account = new Account(
            _settings.CloudName,
            _settings.ApiKey,
            _settings.ApiSecret);

        var cloudinary = new Cloudinary(account);
        cloudinary.Api.Secure = true;
        return cloudinary;
    }
}
