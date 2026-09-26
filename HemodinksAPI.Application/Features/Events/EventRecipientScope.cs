using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public static class EventRecipientScope
{
    public static IQueryable<User> ActiveUsers(IEventFeatureDbContext context, int clinicaId)
    {
        return context.Users.AsNoTracking().Where(user => user.ClinicaId == clinicaId && user.Ativo
            && context.UsuariosClinicas.Any(link => link.UserId == user.Id
                && link.ClinicaId == clinicaId && link.Clinica.Ativa && link.Ativo && link.UsuarioGlobal.Ativo));
    }

    public static IQueryable<User> AllowedUsers(IEventFeatureDbContext context, CurrentUserContext actor)
    {
        var users = ActiveUsers(context, actor.ClinicaId).Where(user => user.Id != actor.Id);
        if (actor.IsAdministrador || actor.IsController)
            return users.Where(user => user.PerfilId != Perfil.PacientesId);
        if (actor.IsMedico)
            return users.Where(user => user.PerfilId == Perfil.AdministradorId
                || user.PerfilId == Perfil.SuperAdministradorId || user.PerfilId == Perfil.ControllerId);
        if (actor.IsEquipe && actor.EquipeId.HasValue)
            return users.Where(user => context.EquipeMembros.Any(member => member.ClinicaId == actor.ClinicaId
                && member.EquipeId == actor.EquipeId && member.Equipe.UsuarioLoginId == actor.Id
                && member.Equipe.Ativa && member.Ativo && member.UserId == user.Id));
        return users.Where(_ => false);
    }

    public static IQueryable<User> MedicalUsers(IEventFeatureDbContext context, CurrentUserContext actor)
    {
        var users = ActiveUsers(context, actor.ClinicaId).Where(user => user.PerfilId == Perfil.MedicosId);
        if (actor.IsPaciente) return users.Where(_ => false);
        if (actor.IsEquipe)
            return users.Where(user => actor.EquipeId.HasValue && context.EquipeMembros.Any(member =>
                member.ClinicaId == actor.ClinicaId && member.EquipeId == actor.EquipeId
                && member.Equipe.UsuarioLoginId == actor.Id && member.Equipe.Ativa
                && member.Ativo && member.UserId == user.Id));
        return users;
    }

    public static IQueryable<GrupoMedico> AllowedGroups(IEventFeatureDbContext context, CurrentUserContext actor)
    {
        var groups = context.GruposMedicos.AsNoTracking()
            .Where(group => group.ClinicaId == actor.ClinicaId && group.Ativo);
        if (actor.IsAdministrador || actor.IsController) return groups;
        if (actor.IsMedico)
            return groups.Where(group => group.Membros.Any(member => member.UserId == actor.Id
                && member.ClinicaId == actor.ClinicaId));
        return groups.Where(_ => false);
    }
}
