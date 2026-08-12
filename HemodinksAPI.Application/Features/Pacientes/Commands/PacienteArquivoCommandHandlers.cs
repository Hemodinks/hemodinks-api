using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class UploadPacienteArquivoCommandHandler : IRequestHandler<UploadPacienteArquivoCommand, PacienteArquivoDto>
{
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<UploadPacienteArquivoCommandHandler> _logger;

    public UploadPacienteArquivoCommandHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<UploadPacienteArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task<PacienteArquivoDto> Handle(UploadPacienteArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == request.PacienteId, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            if (!await PacienteCommandAccess.CanManagePacienteArquivoAsync(_context, paciente, request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId, cancellationToken))
            {
                throw new UnauthorizedAccessException("Sem permissao para enviar arquivo do paciente");
            }

            var storedFile = await _patientFileStorage.SaveAsync(request.File, cancellationToken);
            var arquivo = new PacienteArquivo
            {
                ClinicaId = paciente.ClinicaId,
                PacienteId = request.PacienteId,
                NomeOriginal = storedFile.OriginalName,
                ContentType = storedFile.ContentType,
                TamanhoBytes = storedFile.SizeBytes,
                Url = storedFile.Url,
                DataUpload = DateTime.UtcNow
            };

            _context.PacienteArquivos.Add(arquivo);
            paciente.User.DataAtualizacao = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToArquivoDto(arquivo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar arquivo do paciente: {PacienteId}", request.PacienteId);
            throw;
        }
    }
}

public class DeletePacienteArquivoCommandHandler : IRequestHandler<DeletePacienteArquivoCommand>
{
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteArquivoCommandHandler> _logger;

    public DeletePacienteArquivoCommandHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<DeletePacienteArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeletePacienteArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var arquivo = await _context.PacienteArquivos
                .Include(a => a.Paciente)
                .ThenInclude(p => p.User)
                .FirstOrDefaultAsync(a => a.Id == request.ArquivoId && a.PacienteId == request.PacienteId, cancellationToken);

            if (arquivo == null)
            {
                throw new KeyNotFoundException("Arquivo nao encontrado");
            }

            if (!await PacienteCommandAccess.CanManagePacienteArquivoAsync(_context, arquivo.Paciente, request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId, cancellationToken))
            {
                throw new UnauthorizedAccessException("Sem permissao para excluir arquivo do paciente");
            }

            var fileUrl = arquivo.Url;
            arquivo.Paciente.User.DataAtualizacao = DateTime.UtcNow;
            _context.PacienteArquivos.Remove(arquivo);
            await _context.SaveChangesAsync(cancellationToken);
            await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir arquivo {ArquivoId} do paciente {PacienteId}", request.ArquivoId, request.PacienteId);
            throw;
        }
    }
}
