using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Features.Pacientes.Observacoes;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    private static void ApplyCurrentUser(CreatePacienteCommand command, CurrentUserContext currentUser)
    {
        command.CurrentUserId = currentUser.Id;
        command.CurrentPerfilId = currentUser.PerfilId;
        command.CurrentUserName = currentUser.Nome;
        command.CurrentEquipeId = currentUser.EquipeId;
    }

    private static void ApplyCurrentUser(UpdatePacienteCommand command, CurrentUserContext currentUser)
    {
        command.CurrentUserId = currentUser.Id;
        command.CurrentPerfilId = currentUser.PerfilId;
        command.CurrentUserName = currentUser.Nome;
        command.CurrentEquipeId = currentUser.EquipeId;
    }

    private static void ApplyCurrentUser(CreatePacienteObservacaoCommand command, CurrentUserContext currentUser)
    {
        command.CurrentUserId = currentUser.Id;
        command.CurrentPerfilId = currentUser.PerfilId;
        command.CurrentUserName = currentUser.Nome;
    }

    private static CurrentUserContext GetRequiredCurrentUser(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.ToCurrentUserContext()
            ?? throw new UnauthorizedAccessException("Usuario autenticado invalido");
    }
}
