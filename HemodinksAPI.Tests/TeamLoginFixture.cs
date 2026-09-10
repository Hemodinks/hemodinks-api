using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

internal sealed record LoginTeam(int Id, int UserId, int OperatorId, int MemberUserId, string Email, string Mode, int ClinicId, string Slug);

internal sealed record TeamLoginFixture(LoginTeam Selection, LoginTeam Pin, LoginTeam Anonymous, LoginTeam Other)
{
    public int OtherPatientId { get; init; }
    public const string OtherPatientName = "PACIENTE-EXCLUSIVO-CLINICA-B";
    public const string Password = "LoginTest@2026";
    public const string PinValue = "193847";

    public static async Task<TeamLoginFixture> SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        // This fixture represents an established individual account, not its temporary-password first access.
        var individual = await context.Users.SingleAsync(user => user.Email == "gmarcone@gmail.com");
        individual.PrecisaTrocarSenha = false;
        var otherClinic = new Clinica { Nome = "Clinica B Login", Slug = $"login-b-{Guid.NewGuid():N}", Ativa = true };
        context.Clinicas.Add(otherClinic);
        await context.SaveChangesAsync();
        var hasher = new PasswordHasher();

        async Task<LoginTeam> CreateTeam(string mode, int clinicId, string slug)
        {
            var key = Guid.NewGuid().ToString("N");
            var login = new User { ClinicaId = clinicId, Nome = $"Equipe {mode}", Email = $"team-{key}@example.com",
                Telefone = $"+55{Random.Shared.NextInt64(10000000000, 99999999999)}", Senha = hasher.HashPassword(Password),
                PerfilId = Perfil.EquipeId, Ativo = true, PrecisaTrocarSenha = false };
            var member = new User { ClinicaId = clinicId, Nome = $"Funcionario {mode}", Email = $"member-{key}@example.com",
                Telefone = $"+55{Random.Shared.NextInt64(10000000000, 99999999999)}", Senha = hasher.HashPassword(Password),
                PerfilId = Perfil.MedicosId, Ativo = true, PrecisaTrocarSenha = false };
            var team = new Equipe { ClinicaId = clinicId, Nome = login.Nome, UsuarioLogin = login, ModoIdentificacao = mode };
            var op = new EquipeOperador { ClinicaId = clinicId, Equipe = team, User = member,
                PinHash = mode == EquipeModosIdentificacao.Pin ? hasher.HashPassword(PinValue) : null };
            context.AddRange(login, member, team, op,
                new EquipeMembro { ClinicaId = clinicId, Equipe = team, User = member });
            await context.SaveChangesAsync();
            await GlobalIdentityService.EnsureForUserAsync(context, login, CancellationToken.None);
            await GlobalIdentityService.EnsureForUserAsync(context, member, CancellationToken.None);
            return new LoginTeam(team.Id, login.Id, op.Id, member.Id, login.Email, mode, clinicId, slug);
        }

        var fixture = new TeamLoginFixture(
            await CreateTeam(EquipeModosIdentificacao.Selecao, Clinica.DefaultId, Clinica.DefaultSlug),
            await CreateTeam(EquipeModosIdentificacao.Pin, Clinica.DefaultId, Clinica.DefaultSlug),
            await CreateTeam(EquipeModosIdentificacao.Nenhuma, Clinica.DefaultId, Clinica.DefaultSlug),
            await CreateTeam(EquipeModosIdentificacao.Pin, otherClinic.Id, otherClinic.Slug));
        var patientUser = new User { ClinicaId = otherClinic.Id, Nome = OtherPatientName,
            Email = $"patient-{Guid.NewGuid():N}@example.com", Telefone = "+5511998881234",
            Senha = hasher.HashPassword(Password), PerfilId = Perfil.PacientesId, Ativo = true };
        var patient = new Paciente { ClinicaId = otherClinic.Id, NomePaciente = OtherPatientName,
            User = patientUser, MedicoUserId = fixture.Other.MemberUserId, Medico = "Medico B" };
        context.AddRange(patientUser, patient,
            new FaturamentoMedico { ClinicaId = otherClinic.Id, Paciente = patient, Observacoes = OtherPatientName },
            new Event { ClinicaId = otherClinic.Id, UserId = fixture.Other.UserId, Title = OtherPatientName,
                Start = DateTime.UtcNow.AddDays(1), End = DateTime.UtcNow.AddDays(1).AddHours(1) });
        await context.SaveChangesAsync();
        return fixture with { OtherPatientId = patient.Id };
    }
}
