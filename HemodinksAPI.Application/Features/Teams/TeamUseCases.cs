using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Teams;

public sealed partial class TeamUseCases(
    ITeamDbContext context,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ILicencaService licencaService)
{
    public Task<List<TeamResponse>> ListAsync(CancellationToken cancellationToken) =>
        context.Equipes.AsNoTracking().OrderBy(item => item.Nome)
            .Select(item => new TeamResponse(item.Id, item.Nome, item.UsuarioLoginId, item.UsuarioLogin.Email,
                item.ModoIdentificacao, item.Ativa,
                item.Membros.Where(member => member.Ativo).Select(member => new TeamMemberResponse(
                    member.UserId, member.User.Nome, member.User.Email, member.User.PerfilId,
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Id).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.Ativo).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PinHash != null).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.PrecisaTrocarPin).FirstOrDefault(),
                    item.Operadores.Where(op => op.UserId == member.UserId).Select(op => op.BloqueadoAte).FirstOrDefault()
                )).ToList()))
            .ToListAsync(cancellationToken);

    public async Task<TeamUseCaseResult<int>> CreateAsync(
        CurrentUserContext currentUser,
        CreateTeamInput input,
        CancellationToken cancellationToken)
    {
        var name = RequireText(input.Name, 120, "Nome da equipe obrigatorio");
        var email = GlobalIdentityService.NormalizeEmail(RequireText(input.Email, 255, "Email da equipe obrigatorio"));
        var password = RequireText(input.Password, 200, "Senha da equipe obrigatoria");
        if (password.Length < 8) return TeamUseCaseResult<int>.BadRequest("Senha da equipe deve possuir ao menos 8 caracteres");
        if (await context.Users.AnyAsync(item => item.Email == email, cancellationToken))
            return TeamUseCaseResult<int>.Conflict("Email da equipe ja cadastrado nesta clinica");
        if (await context.UsuariosGlobais.AnyAsync(item => item.Email == email, cancellationToken))
            return TeamUseCaseResult<int>.Conflict("Email coletivo ja utilizado por outra identidade");

        var user = new User
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = name,
            Email = email,
            Telefone = string.IsNullOrWhiteSpace(input.Phone) ? $"+558{DateTime.UtcNow.Ticks % 10_000_000_000:D10}" : input.Phone.Trim(),
            Senha = passwordHasher.HashPassword(password),
            PerfilId = Perfil.EquipeId,
            Ativo = true,
            PrecisaTrocarSenha = false,
            DataCadastro = DateTime.UtcNow
        };
        var team = new Equipe
        {
            ClinicaId = currentUser.ClinicaId,
            Nome = name,
            UsuarioLogin = user,
            ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(input.IdentificationMode),
            Ativa = true
        };
        context.Users.Add(user);
        context.Equipes.Add(team);
        await context.SaveChangesAsync(cancellationToken);
        await GlobalIdentityService.EnsureForUserAsync(context, user, cancellationToken);
        return TeamUseCaseResult<int>.Success(team.Id, TeamAudit.Create("team.create", "team", team.Id, team.ClinicaId,
            new Dictionary<string, object?> { ["nome"] = team.Nome, ["modoIdentificacao"] = team.ModoIdentificacao, ["usuarioLoginId"] = team.UsuarioLoginId }));
    }

    public async Task<TeamUseCaseResult> UpdateAsync(int id, UpdateTeamInput input, CancellationToken cancellationToken)
    {
        var team = await context.Equipes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (team == null) return TeamUseCaseResult.NotFound();
        if (input.Name != null) team.Nome = RequireText(input.Name, 120, "Nome da equipe invalido");
        if (input.IdentificationMode != null) team.ModoIdentificacao = EquipeAuthenticationRules.NormalizeModo(input.IdentificationMode);
        if (input.Active.HasValue) team.Ativa = input.Active.Value;
        team.VersaoSessao++;
        team.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return TeamUseCaseResult.Success(TeamAudit.Create("team.update", "team", team.Id, team.ClinicaId,
            new Dictionary<string, object?> { ["nome"] = team.Nome, ["modoIdentificacao"] = team.ModoIdentificacao, ["ativa"] = team.Ativa }));
    }

}
