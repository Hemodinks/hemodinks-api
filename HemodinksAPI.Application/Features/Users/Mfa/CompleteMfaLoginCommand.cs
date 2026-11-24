using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Tenancy;
using MediatR;

namespace HemodinksAPI.Application.Features.Users.Mfa;

// #150: segundo passo do login. So o desafio (emitido apos a senha correta) e o codigo; a resposta e a mesma do
// login sem MFA e a sessao e aberta pelo endpoint do mesmo jeito.
public sealed class CompleteMfaLoginCommand : IRequest<AuthenticateUserResponse>
{
    public string? Desafio { get; set; }
    public string? Codigo { get; set; }
}

public sealed class CompleteMfaLoginCommandHandler(
    MfaService mfa,
    ILicencaService licencaService,
    IJwtTokenService jwtTokenService,
    IClinicaContext clinicaContext) : IRequestHandler<CompleteMfaLoginCommand, AuthenticateUserResponse>
{
    public async Task<AuthenticateUserResponse> Handle(CompleteMfaLoginCommand request, CancellationToken cancellationToken)
    {
        var currentClinicaId = clinicaContext.GetRequiredClinicaId();
        var verified = await mfa.VerifyLoginChallengeAsync(request.Desafio, request.Codigo, cancellationToken);
        var user = verified.User;
        var authentication = verified.Authentication;
        var licenca = await licencaService.GetCurrentAsync(
            new CurrentUserContext(user.Id, user.PerfilId, user.Nome, user.ClinicaId, user.Clinica.Slug,
                authentication.UsuarioGlobal.Id, authentication.UsuarioClinica.Id),
            cancellationToken);
        var token = jwtTokenService.GenerateToken(authentication.UsuarioGlobal, authentication.UsuarioClinica, user);
        return AuthenticateUserCommandHandler.BuildResponse(user, authentication, currentClinicaId, token, licenca, null);
    }
}
