using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Authentication;

public sealed record GlobalAuthenticationContext(
    UsuarioGlobal UsuarioGlobal,
    UsuarioClinica UsuarioClinica);

public static class GlobalIdentityService
{
    public static async Task<GlobalAuthenticationContext?> AuthenticateAsync(
        IGlobalIdentityDbContext context,
        IPasswordHasher passwordHasher,
        User user,
        string password,
        CancellationToken cancellationToken)
    {
        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (membership == null)
        {
            membership = await EnsureForUserAsync(context, user, cancellationToken);
        }

        if (!membership.Ativo || !membership.UsuarioGlobal.Ativo)
        {
            return null;
        }

        if (!passwordHasher.VerifyPassword(password, membership.UsuarioGlobal.Senha))
        {
            // Um unico fallback e permitido apenas para identidades migradas que ainda nao
            // tiveram sua credencial global confirmada. Depois disso a senha global e canonica.
            if (membership.UsuarioGlobal.DataAtualizacao.HasValue
                || !passwordHasher.VerifyPassword(password, user.Senha))
            {
                return null;
            }

            // Compatibilidade de transicao: a credencial local validada passa a ser a global.
            membership.UsuarioGlobal.Senha = user.Senha;
            membership.UsuarioGlobal.DataAtualizacao = DateTime.UtcNow;
        }

        membership.PerfilId = user.PerfilId;
        membership.Ativo = user.Ativo;
        membership.DataAtualizacao = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return new GlobalAuthenticationContext(membership.UsuarioGlobal, membership);
    }

    public static async Task<UsuarioClinica> EnsureForUserAsync(
        IGlobalIdentityDbContext context,
        User user,
        CancellationToken cancellationToken,
        bool clinicaPadrao = false)
    {
        var existingMembership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (existingMembership != null)
        {
            SynchronizeMembership(existingMembership, user);
            await context.SaveChangesAsync(cancellationToken);
            return existingMembership;
        }

        var normalizedEmail = NormalizeEmail(user.Email);
        var globalUser = await context.UsuariosGlobais
            .FirstOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);
        if (globalUser == null)
        {
            globalUser = new UsuarioGlobal
            {
                Nome = user.Nome,
                Email = normalizedEmail,
                Senha = user.Senha,
                Ativo = user.Ativo,
                DataCadastro = DateTime.UtcNow,
                DataAtualizacao = DateTime.UtcNow
            };
            context.UsuariosGlobais.Add(globalUser);
        }

        if (clinicaPadrao && globalUser.Id != 0)
        {
            var previousDefaults = await context.UsuariosClinicas
                .Where(item => item.UsuarioGlobalId == globalUser.Id && item.ClinicaPadrao)
                .ToListAsync(cancellationToken);
            foreach (var previousDefault in previousDefaults)
            {
                previousDefault.ClinicaPadrao = false;
                previousDefault.DataAtualizacao = DateTime.UtcNow;
            }
        }

        var membership = new UsuarioClinica
        {
            UsuarioGlobal = globalUser,
            ClinicaId = user.ClinicaId,
            UserId = user.Id,
            PerfilId = user.PerfilId,
            Ativo = user.Ativo,
            ClinicaPadrao = clinicaPadrao || globalUser.Id == 0 || !await context.UsuariosClinicas
                .AnyAsync(item => item.UsuarioGlobalId == globalUser.Id, cancellationToken),
            DataCadastro = DateTime.UtcNow
        };
        context.UsuariosClinicas.Add(membership);
        await context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public static async Task SynchronizeUserAsync(
        IGlobalIdentityDbContext context,
        User user,
        CancellationToken cancellationToken)
    {
        var membership = await context.UsuariosClinicas
            .Include(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (membership == null)
        {
            return;
        }

        SynchronizeMembership(membership, user);
    }

    private static void SynchronizeMembership(UsuarioClinica membership, User user)
    {
        membership.PerfilId = user.PerfilId;
        membership.Ativo = user.Ativo;
        membership.DataAtualizacao = DateTime.UtcNow;

        if (user.Ativo)
        {
            membership.UsuarioGlobal.Ativo = true;
        }

        membership.UsuarioGlobal.Nome = user.Nome;
        membership.UsuarioGlobal.DataAtualizacao = DateTime.UtcNow;
    }

    public static async Task SynchronizePasswordAsync(
        IGlobalIdentityDbContext context,
        int userId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        var globalUser = await context.UsuariosClinicas
            .Where(item => item.UserId == userId)
            .Select(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(cancellationToken);
        if (globalUser == null)
        {
            return;
        }

        globalUser.Senha = passwordHash;
        globalUser.DataAtualizacao = DateTime.UtcNow;
    }

    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
