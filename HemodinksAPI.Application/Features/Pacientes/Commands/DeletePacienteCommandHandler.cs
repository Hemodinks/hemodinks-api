using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class DeletePacienteCommandHandler : IRequestHandler<DeletePacienteCommand>
{
    private readonly IPatientFeatureDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteCommandHandler> _logger;

    public DeletePacienteCommandHandler(
        IPatientFeatureDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IPatientFileStorage patientFileStorage,
        ILogger<DeletePacienteCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeletePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.CurrentPerfilId is not (
                Perfil.AdministradorId
                or Perfil.SuperAdministradorId
                or Perfil.MedicosId))
            {
                throw new UnauthorizedAccessException("Sem permissao para excluir paciente");
            }

            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            var fotoPerfil = paciente.User.FotoPerfil;
            var fileUrls = paciente.Arquivos.Select(arquivo => arquivo.Url).ToList();

            _context.Users.Remove(paciente.User);
            await _context.SaveChangesAsync(cancellationToken);

            await _profilePhotoStorage.DeleteAsync(fotoPerfil, cancellationToken);

            foreach (var fileUrl in fileUrls)
            {
                await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}
