namespace WebDatLichKhamBenh.Application.DTOs.Images;

public sealed record ImageUploadDto(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
