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
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;

    public DownloadUserArquivoQueryHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
    }

    public async Task<PrivateFileDownload?> Handle(
        DownloadUserArquivoQuery request,
        CancellationToken cancellationToken)
    {
        UserQueryAccess.EnsureCanAccessUser(request.CurrentUser, request.UserId);

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

        var storedFile = await _patientFileStorage.GetAsync(arquivo.Url, cancellationToken);
        return storedFile == null
            ? null
            : new PrivateFileDownload
            {
                Content = storedFile.Content,
                ContentType = arquivo.ContentType,
                FileName = arquivo.NomeOriginal
            };
    }
}
