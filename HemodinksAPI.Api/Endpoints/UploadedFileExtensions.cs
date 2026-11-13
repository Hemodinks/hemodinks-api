using HemodinksAPI.Application.Storage;

namespace HemodinksAPI.Api;

public static class UploadedFileExtensions
{
    public static UploadedFile ToUploadedFile(this IFormFile file)
    {
        return new UploadedFile(file.FileName, file.ContentType, file.Length, file.OpenReadStream);
    }
}
