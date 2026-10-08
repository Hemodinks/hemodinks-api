using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    [Fact]
    public async Task PasswordPolicy_PatientCreationFailsClosedBeforeSavingGeneratedCredential()
    {
        await using var context = TestDbContextFactory.Create();
        var lookup = new UnavailablePatientPasswordLookup();
        var handler = new CreatePacienteCommandHandler(new NewPasswordPolicy(lookup), context,
            CreateCbhpmCache(context), new PasswordHasher(), new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);
        await Assert.ThrowsAsync<PasswordPolicyUnavailableException>(() => handler.Handle(new CreatePacienteCommand {
            NomePaciente = "Paciente teste", Email = "policy-patient@example.com",
            CurrentPerfilId = Perfil.AdministradorId
        }, default));
        Assert.Equal(1, lookup.Calls);
        Assert.False(await context.Users.AnyAsync(x => x.Email == "policy-patient@example.com"));
        Assert.False(await context.Pacientes.AnyAsync());
    }

    private sealed class UnavailablePatientPasswordLookup : ICompromisedPasswordLookup
    {
        public int Calls;
        public PasswordLookupResult Check(string candidate) { Calls++; return PasswordLookupResult.Unavailable; }
    }
}
