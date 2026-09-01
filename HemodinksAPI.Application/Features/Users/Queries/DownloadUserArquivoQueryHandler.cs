using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public sealed record DownloadUserArquivoQuery(
    int UserId,
    int ArquivoId,
    CurrentUserContext CurrentUser) : IRequest<PrivateFileDownload?>;

public sealed class DownloadUserArquivoQueryHandler
    : IRequestHandler<DownloadUserArquivoQuery, PrivateFileDownload?>
{
    private readonly IUserFeatureDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;

    public DownloadUserArquivoQueryHandler(
        IUserFeatureDbContext context,
        IPatientFileStorage patientFileStorage)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
    }

    public async Task<PrivateFileDownload?> Handle(
        DownloadUserArquivoQuery request,
        CancellationToken cancellationToken)
    {
        await UserQueryAccess.EnsureCanAccessUserAsync(_context, request.CurrentUser, request.UserId, cancellationToken);

        var arquivo = await _context.UserArquivos
            .AsNoTracking()
            .Where(item => item.Id == request.ArquivoId && item.UserId == request.UserId)
            .Select(item => new
            {
                item.Url,
                item.NomeOriginal,
                item.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (arquivo == null)
        {
            return null;
        }

        var storedFile = await _patientFileStorage.GetAsync(arquivo.Url, cancellationToken)
            ?? throw new StoredFileUnavailableException(
                "Arquivo do usuario registrado nao foi localizado no armazenamento.");

        return new PrivateFileDownload
        {
            Content = storedFile.Content,
            ContentType = arquivo.ContentType,
            FileName = arquivo.NomeOriginal
        };
    }
}
