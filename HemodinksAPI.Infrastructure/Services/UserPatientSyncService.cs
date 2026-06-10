using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Services;

public class UserPatientSyncService : IUserPatientSyncService
{
    private readonly IAppDbContext _context;

    public UserPatientSyncService(IAppDbContext context)
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
                UserId = user.Id,
                NomePaciente = user.Nome
            });

            return;
        }

        paciente.NomePaciente = user.Nome;
    }
}
