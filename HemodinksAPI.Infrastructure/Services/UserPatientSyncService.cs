using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Services;

public class UserPatientSyncService : IUserPatientSyncService
{
    private readonly IPatientFeatureDbContext _context;

    public UserPatientSyncService(IPatientFeatureDbContext context)
    {
        _context = context;
    }

    public async Task EnsurePacienteForUserAsync(User user, CancellationToken cancellationToken)
    {
        if (user.PerfilId != Perfil.PacientesId)
        {
            return;
        }

        var paciente = await _context.Pacientes
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (paciente == null)
        {
            _context.Pacientes.Add(new Paciente
            {
                ClinicaId = user.ClinicaId,
                UserId = user.Id,
                NomePaciente = user.Nome
            });

            return;
        }

        paciente.NomePaciente = user.Nome;
    }
}
