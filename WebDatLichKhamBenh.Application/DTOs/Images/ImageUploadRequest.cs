namespace WebDatLichKhamBenh.Application.DTOs.Images;

public sealed record ImageUploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
