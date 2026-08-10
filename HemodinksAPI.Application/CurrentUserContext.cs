using System.Security.Claims;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Authentication;

namespace HemodinksAPI.Application.Authorization;

public sealed record CurrentUserContext(
    int Id,
    int PerfilId,
    string Nome,
    int ClinicaId = Clinica.DefaultId,
    string ClinicaSlug = Clinica.DefaultSlug,
    int UsuarioGlobalId = 0,
    int UsuarioClinicaId = 0,
    int? EquipeId = null,
    int? EquipeOperadorId = null,
    bool IdentificacaoConfiavel = false)
{
    public bool IsAdministrador => Perfil.IsAdministradorOuSuper(PerfilId);

    public bool IsSuperAdministrador => PerfilId == Perfil.SuperAdministradorId;

    public bool IsMedico => PerfilId == Perfil.MedicosId;

    public bool IsPaciente => PerfilId == Perfil.PacientesId;

    public bool IsController => PerfilId == Perfil.ControllerId;

    public bool IsEquipe => PerfilId == Perfil.EquipeId;
}

public static class CurrentUserContextExtensions
{
    public static CurrentUserContext? ToCurrentUserContext(this ClaimsPrincipal claimsPrincipal)
    {
        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var perfilIdClaim = claimsPrincipal.FindFirst("perfilId")?.Value;
        var nome = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var clinicaIdClaim = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaId)?.Value;
        var clinicaSlug = claimsPrincipal.FindFirst(ClinicaClaimTypes.ClinicaSlug)?.Value ?? Clinica.DefaultSlug;
        var usuarioGlobalIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioGlobalId)?.Value;
        var usuarioClinicaIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.UsuarioClinicaId)?.Value;
        var equipeIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeId)?.Value;
        var equipeOperadorIdClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.EquipeOperadorId)?.Value;
        var identificacaoConfiavelClaim = claimsPrincipal.FindFirst(GlobalIdentityClaimTypes.IdentificacaoConfiavel)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || !int.TryParse(perfilIdClaim, out var perfilId))
        {
            return null;
        }

        var clinicaId = int.TryParse(clinicaIdClaim, out var parsedClinicaId) && parsedClinicaId > 0
            ? parsedClinicaId
            : Clinica.DefaultId;

        _ = int.TryParse(usuarioGlobalIdClaim, out var usuarioGlobalId);
        _ = int.TryParse(usuarioClinicaIdClaim, out var usuarioClinicaId);
        var equipeId = int.TryParse(equipeIdClaim, out var parsedEquipeId) && parsedEquipeId > 0 ? parsedEquipeId : (int?)null;
        var equipeOperadorId = int.TryParse(equipeOperadorIdClaim, out var parsedOperadorId) && parsedOperadorId > 0 ? parsedOperadorId : (int?)null;
        var identificacaoConfiavel = bool.TryParse(identificacaoConfiavelClaim, out var parsedConfiavel) && parsedConfiavel;

        return new CurrentUserContext(
            userId,
            perfilId,
            nome,
            clinicaId,
            clinicaSlug,
            usuarioGlobalId,
            usuarioClinicaId,
            equipeId,
            equipeOperadorId,
            identificacaoConfiavel);
    }
}
