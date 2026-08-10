using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public sealed record DownloadPacienteArquivoQuery(
    int PacienteId,
    int ArquivoId,
    int CurrentUserId,
    int CurrentPerfilId,
    int? CurrentEquipeId = null) : IRequest<PrivateFileDownload?>;

public sealed class DownloadPacienteArquivoQueryHandler
    : IRequestHandler<DownloadPacienteArquivoQuery, PrivateFileDownload?>
{
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;

    public DownloadPacienteArquivoQueryHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
    }

    public async Task<PrivateFileDownload?> Handle(
        DownloadPacienteArquivoQuery request,
        CancellationToken cancellationToken)
    {
        var accessiblePatientIds = PacienteAccess.ApplyScope(
                _context,
                _context.Pacientes.AsNoTracking(),
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentEquipeId)
            .Select(paciente => paciente.Id);

        var arquivo = await _context.PacienteArquivos
            .AsNoTracking()
            .Where(item => item.Id == request.ArquivoId
                && item.PacienteId == request.PacienteId
                && accessiblePatientIds.Contains(item.PacienteId))
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
