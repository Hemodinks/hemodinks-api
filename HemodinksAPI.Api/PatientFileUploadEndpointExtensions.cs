using Microsoft.AspNetCore.Mvc;

namespace HemodinksAPI.Api;

internal static class PatientFileUploadEndpointExtensions
{
    // 10 MiB de arquivo mais margem apenas para os cabeçalhos do multipart.
    internal const long MaxFileBytes = 10 * 1024 * 1024;
    internal const long MaxRequestBodyBytes = MaxFileBytes + 256 * 1024;

    public static RouteHandlerBuilder LimitPatientFileUpload(this RouteHandlerBuilder builder)
    {
        return builder.WithMetadata(
            new RequestSizeLimitAttribute(MaxRequestBodyBytes),
            new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = MaxRequestBodyBytes,
                ValueLengthLimit = 16 * 1024,
                KeyLengthLimit = 2 * 1024,
                MultipartHeadersLengthLimit = 16 * 1024
            });
    }
}
