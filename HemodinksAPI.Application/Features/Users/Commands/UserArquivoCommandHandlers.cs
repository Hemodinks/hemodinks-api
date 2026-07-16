using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

public class UploadUserArquivoCommandHandler : IRequestHandler<UploadUserArquivoCommand, UserArquivoDto>
{
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<UploadUserArquivoCommandHandler> _logger;

    public UploadUserArquivoCommandHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<UploadUserArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task<UserArquivoDto> Handle(UploadUserArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.CurrentUser != null && !UserCommandAccess.CanManageUserFiles(request.CurrentUser, request.UserId))
            {
                throw new UnauthorizedAccessException("Sem permissao para enviar arquivo do usuario");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            if (user.PerfilId != Perfil.MedicosId)
            {
                throw new InvalidOperationException("Documentos de cadastro estao disponiveis apenas para medicos");
            }

            var storedFile = await _patientFileStorage.SaveAsync(request.File, cancellationToken);
            var arquivo = new UserArquivo
            {
                ClinicaId = user.ClinicaId,
                UserId = request.UserId,
                NomeOriginal = storedFile.OriginalName,
                ContentType = storedFile.ContentType,
                TamanhoBytes = storedFile.SizeBytes,
                Url = storedFile.Url,
                DataUpload = DateTime.UtcNow
            };

            user.DataAtualizacao = DateTime.UtcNow;
            _context.UserArquivos.Add(arquivo);
            await _context.SaveChangesAsync(cancellationToken);

            return UserQueryMapper.ToArquivoDto(arquivo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar arquivo do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

public class DeleteUserArquivoCommandHandler : IRequestHandler<DeleteUserArquivoCommand>
{
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeleteUserArquivoCommandHandler> _logger;

    public DeleteUserArquivoCommandHandler(
        IAppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<DeleteUserArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeleteUserArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.CurrentUser != null && !UserCommandAccess.CanManageUserFiles(request.CurrentUser, request.UserId))
            {
                throw new UnauthorizedAccessException("Sem permissao para excluir arquivo do usuario");
            }

            var arquivo = await _context.UserArquivos
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == request.ArquivoId && a.UserId == request.UserId, cancellationToken);

            if (arquivo == null)
            {
                throw new KeyNotFoundException("Arquivo nao encontrado");
            }

            var fileUrl = arquivo.Url;
            arquivo.User.DataAtualizacao = DateTime.UtcNow;
            _context.UserArquivos.Remove(arquivo);
            await _context.SaveChangesAsync(cancellationToken);
            await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir arquivo {ArquivoId} do usuario {UserId}", request.ArquivoId, request.UserId);
            throw;
        }
    }
}
