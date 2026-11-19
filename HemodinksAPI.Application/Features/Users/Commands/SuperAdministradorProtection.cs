using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Cadastro de quem é SuperAdministrador (em qualquer clínica) só pode ser alterado, inativado, excluído ou ter
/// senha temporária gerada por outro SuperAdministrador. A identidade (nome, senha) é global, então um perfil
/// inferior na clínica atual não pode mexer em quem é SuperAdministrador em outra clínica.
/// </summary>
internal static class SuperAdministradorProtection
{
    /// <summary>
    /// Verifica a hierarquia em todas as clínicas sem expor dados delas: devolve só um booleano.
    /// </summary>
    public static async Task<bool> IsSuperAdministradorInAnyClinicAsync<TContext>(
        TContext context,
        string email,
        int? usuarioGlobalId,
        CancellationToken cancellationToken)
        where TContext : IUserDbContext, IGlobalIdentityDbContext
    {
        // Vínculos da identidade global primeiro (indexado por UsuarioGlobalId); o e-mail cobre cadastro sem vínculo.
        if (usuarioGlobalId is { } globalId
            && await context.UsuariosClinicas.IgnoreQueryFilters()
                .AnyAsync(x => x.UsuarioGlobalId == globalId && x.User.PerfilId == Perfil.SuperAdministradorId, cancellationToken))
        {
            return true;
        }

        var normalizedEmail = GlobalIdentityService.NormalizeEmail(email);
        return await context.Users.IgnoreQueryFilters().AnyAsync(x => x.PerfilId == Perfil.SuperAdministradorId
            && x.Email.Trim().ToLower() == normalizedEmail, cancellationToken);
    }

    /// <summary>
    /// True quando o ator não pode alterar o alvo: ator não é SuperAdministrador, não é o próprio titular
    /// e o alvo é SuperAdministrador aqui ou em outra clínica.
    /// </summary>
    public static async Task<bool> IsProtectedFromAsync<TContext>(
        TContext context,
        User target,
        CurrentUserContext? actor,
        CancellationToken cancellationToken)
        where TContext : IUserDbContext, IGlobalIdentityDbContext
    {
        if (actor?.IsSuperAdministrador == true || (actor != null && actor.Id == target.Id))
        {
            return false;
        }

        if (target.PerfilId == Perfil.SuperAdministradorId)
        {
            return true;
        }

        var usuarioGlobalId = await context.UsuariosClinicas
            .Where(item => item.UserId == target.Id)
            .Select(item => (int?)item.UsuarioGlobalId)
            .FirstOrDefaultAsync(cancellationToken);
        return await IsSuperAdministradorInAnyClinicAsync(context, target.Email, usuarioGlobalId, cancellationToken);
    }
}
