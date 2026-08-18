using HemodinksAPI.Domain.Models;

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
