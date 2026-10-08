using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public partial class AppDbContext
{
    public async Task RegisterOperatorPinFailureAsync(int operatorId, int teamId, int clinicId,
        int expectedVersion, DateTime now, CancellationToken cancellationToken)
    {
        var eligible = EquipeOperadores.Where(item => item.Id == operatorId && item.EquipeId == teamId
            && item.ClinicaId == clinicId && item.Ativo && item.VersaoSessao == expectedVersion
            && (item.BloqueadoAte == null || item.BloqueadoAte <= now));
        if (Database.IsRelational())
        {
            var blockedUntil = now.AddMinutes(15);
            // Every expression reads the same pre-update row. SQL serializes concurrent
            // failures, and a committed lock cannot be extended by an in-flight attempt.
            await eligible.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.TentativasFalhas, item => item.TentativasFalhas + 1 >= 5 ? 0 : item.TentativasFalhas + 1)
                .SetProperty(item => item.BloqueadoAte, item => item.TentativasFalhas + 1 >= 5 ? blockedUntil : item.BloqueadoAte)
                .SetProperty(item => item.VersaoSessao, item => item.TentativasFalhas + 1 >= 5 ? item.VersaoSessao + 1 : item.VersaoSessao),
                cancellationToken);
            return;
        }

        var op = await eligible.SingleOrDefaultAsync(cancellationToken);
        if (op == null) return;
        op.TentativasFalhas++;
        if (op.TentativasFalhas >= 5)
        {
            op.TentativasFalhas = 0;
            op.BloqueadoAte = now.AddMinutes(15);
            op.VersaoSessao++;
        }
        await SaveChangesAsync(cancellationToken);
    }

    public void MarkOperatorAuthenticationSuccessful(EquipeOperador op) =>
        Entry(op).Property(item => item.TentativasFalhas).IsModified = true;
}
