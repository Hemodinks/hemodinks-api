using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class OperatorLoginProtectionSqlServerTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentPinFailures_PersistOneLock_RejectStaleSuccess_AndRespectTenant()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server for atomic operator protection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create($"HemodinksOperatorProtection_{Guid.NewGuid():N}")).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var login = new User { ClinicaId = Clinica.DefaultId, Nome = "Team protection", Email = "operator-lock@example.invalid",
                Telefone = "11999998767", Senha = "test-hash", PerfilId = Perfil.EquipeId, Ativo = true };
            var member = new User { ClinicaId = Clinica.DefaultId, Nome = "Protected operator", Email = "operator@example.invalid",
                Telefone = "11999998766", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            var team = new Equipe { ClinicaId = Clinica.DefaultId, Nome = "Protected team", UsuarioLogin = login,
                ModoIdentificacao = EquipeModosIdentificacao.Pin };
            var op = new EquipeOperador { ClinicaId = Clinica.DefaultId, Equipe = team, User = member, PinHash = "test-pin-hash" };
            var otherClinic = new Clinica { Nome = "Other operator clinic", Slug = "other-operator-protection", Ativa = true };
            setup.AddRange(login, member, team, op, otherClinic);
            await setup.SaveChangesAsync();

            // A valid login has read counter zero before another replica commits the lock.
            await using var lateSuccess = new PlatformDbContext(options);
            var staleOperator = await lateSuccess.EquipeOperadores.SingleAsync(item => item.Id == op.Id);
            staleOperator.TentativasFalhas = 0;
            staleOperator.BloqueadoAte = null;
            lateSuccess.MarkOperatorAuthenticationSuccessful(staleOperator);

            await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                await using var replica = new PlatformDbContext(options);
                await replica.RegisterOperatorPinFailureAsync(op.Id, team.Id, Clinica.DefaultId, 1, now, default);
            }));

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => lateSuccess.SaveChangesAsync());
            setup.ChangeTracker.Clear();
            var locked = await setup.EquipeOperadores.AsNoTracking().SingleAsync(item => item.Id == op.Id);
            Assert.Equal(0, locked.TentativasFalhas);
            Assert.Equal(2, locked.VersaoSessao);
            Assert.Equal(now.AddMinutes(15), locked.BloqueadoAte);

            // Neither another team nor another clinic can consume the operator's counter.
            await setup.RegisterOperatorPinFailureAsync(op.Id, team.Id + 1, Clinica.DefaultId, 2, now.AddMinutes(16), default);
            await setup.RegisterOperatorPinFailureAsync(op.Id, team.Id, otherClinic.Id, 2, now.AddMinutes(16), default);
            var clinicB = new ClinicaContext();
            clinicB.SetCurrent(otherClinic.Id, otherClinic.Slug);
            await using (var otherTenant = new AppDbContext(options, clinicB))
                await otherTenant.RegisterOperatorPinFailureAsync(op.Id, team.Id, Clinica.DefaultId, 2, now.AddMinutes(16), default);
            Assert.Equal(0, await setup.EquipeOperadores.Where(item => item.Id == op.Id).Select(item => item.TentativasFalhas).SingleAsync());

            // An attempt after expiry starts a fresh count; a successful login can clear it.
            await setup.RegisterOperatorPinFailureAsync(op.Id, team.Id, Clinica.DefaultId, 2, now.AddMinutes(16), default);
            var recovered = await setup.EquipeOperadores.SingleAsync(item => item.Id == op.Id);
            Assert.Equal(1, recovered.TentativasFalhas);
            Assert.Equal(2, recovered.VersaoSessao);
            recovered.TentativasFalhas = 0;
            recovered.BloqueadoAte = null;
            setup.MarkOperatorAuthenticationSuccessful(recovered);
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();
            var reset = await setup.EquipeOperadores.SingleAsync(item => item.Id == op.Id);
            Assert.Equal(0, reset.TentativasFalhas);
            Assert.Null(reset.BloqueadoAte);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
