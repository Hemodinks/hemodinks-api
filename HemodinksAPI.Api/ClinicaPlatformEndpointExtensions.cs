using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Net.Mail;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static partial class ClinicaPlatformEndpointExtensions
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public static void MapClinicaPlatformEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/clinicas")
            .WithTags("Plataforma - Clinicas")
            .RequireAuthorization("SuperAdministrador")
            .AddEndpointFilter(async (invocationContext, next) =>
            {
                try
                {
                    return await next(invocationContext);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        group.MapGet("/", ListClinicas);
        group.MapGet("/{id:int}", GetClinica);
        group.MapPost("/", CreateClinica);
        group.MapPut("/{id:int}", UpdateClinica);

        app.MapGet("/api/platform/auditoria", ListPlatformAudit)
            .WithTags("Plataforma - Auditoria")
            .RequireAuthorization("SuperAdministrador");
    }

    private static async Task<IResult> ListClinicas(AppDbContext context, CancellationToken cancellationToken)
    {
        var items = await context.Clinicas
            .AsNoTracking()
            .OrderBy(item => item.Nome)
            .Select(item => ToResponse(item, null))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetClinica(
        int id,
        AppDbContext context,
        ClinicaContext clinicaContext,
        CancellationToken cancellationToken)
    {
        var clinica = await context.Clinicas.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (clinica == null)
        {
            return Results.NotFound();
        }

        clinicaContext.SetPlatformScope();
        var userCount = await context.Users.CountAsync(item => item.ClinicaId == id, cancellationToken);
        return Results.Ok(ToResponse(clinica, userCount));
    }

    private static async Task<IResult> CreateClinica(
        CreateClinicaRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        AppDbContext context,
        ClinicaContext clinicaContext,
        IPasswordHasher passwordHasher,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var nome = RequireText(request.Nome, "Nome da clinica obrigatorio", 120);
        var slug = NormalizeSlug(request.Slug);
        var adminNome = RequireText(request.AdministradorNome, "Nome do administrador obrigatorio", 255);
        var adminEmail = RequireText(request.AdministradorEmail, "Email do administrador obrigatorio", 255).ToLowerInvariant();
        var adminPassword = RequireText(request.AdministradorSenha, "Senha do administrador obrigatoria", 200);

        if (!MailAddress.TryCreate(adminEmail, out _) || adminPassword.Length < 8)
        {
            throw new InvalidOperationException("Email invalido ou senha com menos de 8 caracteres");
        }

        if (request.LimiteUsuarios is <= 0)
        {
            throw new InvalidOperationException("LimiteUsuarios deve ser maior que zero");
        }

        if (await context.Clinicas.AnyAsync(item => item.Slug == slug, cancellationToken))
        {
            return Results.Conflict(new { message = "Slug da clinica ja cadastrado" });
        }

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var now = DateTime.UtcNow;
        var clinica = new Clinica
        {
            Nome = nome,
            Slug = slug,
            Ativa = true,
            Plano = NormalizeOptional(request.Plano, "Trial", 50),
            AssinaturaStatus = NormalizeOptional(request.AssinaturaStatus, "Trial", 30),
            TrialAte = request.TrialAte ?? now.AddDays(14),
            AssinaturaValidaAte = request.AssinaturaValidaAte,
            LimiteUsuarios = request.LimiteUsuarios,
            DataCadastro = now
        };

        context.Clinicas.Add(clinica);
        await context.SaveChangesAsync(cancellationToken);

        clinicaContext.SetPlatformScope();
        var admin = new User
        {
            ClinicaId = clinica.Id,
            Nome = adminNome,
            Email = adminEmail,
            Telefone = NormalizeOptional(request.AdministradorTelefone, $"+550{clinica.Id:00000000000}", 20),
            Senha = passwordHasher.HashPassword(adminPassword),
            DataCadastro = now,
            Ativo = true,
            PrecisaTrocarSenha = true,
            PerfilId = Perfil.AdministradorId
        };

        context.Users.Add(admin);
        context.ConfiguracoesSistema.Add(new ConfiguracaoSistema
        {
            ClinicaId = clinica.Id,
            NomeEmpresa = clinica.Nome
        });

        var platformShadowUser = await AddPlatformShadowUserAsync(principal, clinica.Id, context, cancellationToken);
        await CloneClinicReferenceDataAsync(clinica.Id, context, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await GlobalIdentityService.EnsureForUserAsync(context, admin, cancellationToken, clinicaPadrao: true);
        if (platformShadowUser != null)
        {
            await GlobalIdentityService.EnsureForUserAsync(context, platformShadowUser, cancellationToken);
        }

        await auditService.RecordAsync(
            httpContext,
            "clinic.create",
            "clinic",
            clinica.Id.ToString(),
            clinica.Id,
            new { clinica.Nome, clinica.Slug, admin.Email },
            true,
            cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var userCount = await context.Users.CountAsync(item => item.ClinicaId == clinica.Id, cancellationToken);
        return Results.Created($"/api/platform/clinicas/{clinica.Id}", ToResponse(clinica, userCount));
    }

    private static async Task<IResult> UpdateClinica(
        int id,
        UpdateClinicaRequest request,
        HttpContext httpContext,
        AppDbContext context,
        PlatformAuditService auditService,
        CancellationToken cancellationToken)
    {
        var clinica = await context.Clinicas.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (clinica == null)
        {
            return Results.NotFound();
        }

        if (request.Nome != null)
        {
            clinica.Nome = RequireText(request.Nome, "Nome da clinica invalido", 120);
        }

        if (request.Slug != null)
        {
            var slug = NormalizeSlug(request.Slug);
            if (await context.Clinicas.AnyAsync(item => item.Id != id && item.Slug == slug, cancellationToken))
            {
                return Results.Conflict(new { message = "Slug da clinica ja cadastrado" });
            }

            clinica.Slug = slug;
        }

        if (request.Ativa.HasValue) clinica.Ativa = request.Ativa.Value;
        if (request.Plano != null) clinica.Plano = NormalizeOptional(request.Plano, "Trial", 50);
        if (request.AssinaturaStatus != null) clinica.AssinaturaStatus = NormalizeOptional(request.AssinaturaStatus, "Trial", 30);
        if (request.TrialAte.HasValue) clinica.TrialAte = request.TrialAte;
        if (request.AssinaturaValidaAte.HasValue) clinica.AssinaturaValidaAte = request.AssinaturaValidaAte;
        if (request.LimiteUsuarios.HasValue)
        {
            if (request.LimiteUsuarios <= 0)
            {
                throw new InvalidOperationException("LimiteUsuarios deve ser maior que zero");
            }

            clinica.LimiteUsuarios = request.LimiteUsuarios;
        }
        clinica.DataAtualizacao = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            httpContext,
            "clinic.update",
            "clinic",
            clinica.Id.ToString(),
            clinica.Id,
            new { clinica.Nome, clinica.Slug, clinica.Ativa, clinica.Plano, clinica.AssinaturaStatus },
            true,
            cancellationToken);
        return Results.Ok(ToResponse(clinica, null));
    }

    private static async Task<User?> AddPlatformShadowUserAsync(
        ClaimsPrincipal principal,
        int clinicaId,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var sourceId = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId) ? parsedId : 0;
        var source = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == sourceId && item.PerfilId == Perfil.SuperAdministradorId, cancellationToken);

        if (source == null || await context.Users.IgnoreQueryFilters().AnyAsync(
                item => item.ClinicaId == clinicaId && item.Email == source.Email,
                cancellationToken))
        {
            return null;
        }

        var shadowUser = new User
        {
            ClinicaId = clinicaId,
            Nome = source.Nome,
            Email = source.Email,
            Telefone = $"+559{clinicaId:00000000000}",
            Cpf = null,
            Senha = source.Senha,
            DataNascimento = source.DataNascimento,
            DataCadastro = DateTime.UtcNow,
            Ativo = true,
            PrecisaTrocarSenha = source.PrecisaTrocarSenha,
            PerfilId = Perfil.SuperAdministradorId
        };
        context.Users.Add(shadowUser);
        return shadowUser;
    }

    private static async Task<IResult> ListPlatformAudit(
        AppDbContext context,
        int? clinicaId,
        string? acao,
        DateTime? de,
        DateTime? ate,
        int pagina = 1,
        int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 200);

        var query = context.AuditoriasPlataforma.AsNoTracking().AsQueryable();
        if (clinicaId.HasValue) query = query.Where(item => item.ClinicaId == clinicaId.Value);
        if (!string.IsNullOrWhiteSpace(acao)) query = query.Where(item => item.Acao == acao.Trim());
        if (de.HasValue) query = query.Where(item => item.DataCadastro >= de.Value);
        if (ate.HasValue) query = query.Where(item => item.DataCadastro <= ate.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.DataCadastro)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(item => new
            {
                item.Id,
                item.UsuarioGlobalId,
                item.ClinicaId,
                item.UserId,
                item.Acao,
                item.Recurso,
                item.EntidadeId,
                item.DetalhesJson,
                item.Ip,
                item.UserAgent,
                item.RequestId,
                item.Sucesso,
                item.DataCadastro
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { pagina, tamanhoPagina, total, items });
    }

    private static async Task CloneClinicReferenceDataAsync(
        int targetClinicaId,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var convenios = await context.Convenios.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ClinicaId == Clinica.DefaultId)
            .Select(item => item.DescricaoConvenio)
            .ToListAsync(cancellationToken);
        context.Convenios.AddRange(convenios.Select(descricao => new Convenio
        {
            ClinicaId = targetClinicaId,
            DescricaoConvenio = descricao
        }));

        var hospitais = await context.Hospitais.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ClinicaId == Clinica.DefaultId)
            .Select(item => item.Nome)
            .ToListAsync(cancellationToken);
        context.Hospitais.AddRange(hospitais.Select(nome => new Hospital { ClinicaId = targetClinicaId, Nome = nome }));

        var fornecedores = await context.OPME.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ClinicaId == Clinica.DefaultId)
            .Select(item => item.Fornecedor)
            .ToListAsync(cancellationToken);
        context.OPME.AddRange(fornecedores.Select(nome => new Opme { ClinicaId = targetClinicaId, Fornecedor = nome }));
    }

    private static ClinicaPlatformResponse ToResponse(Clinica clinica, int? userCount)
    {
        return new ClinicaPlatformResponse(
            clinica.Id,
            clinica.Nome,
            clinica.Slug,
            clinica.Ativa,
            clinica.Plano,
            clinica.AssinaturaStatus,
            clinica.TrialAte,
            clinica.AssinaturaValidaAte,
            clinica.LimiteUsuarios,
            userCount,
            clinica.DataCadastro,
            clinica.DataAtualizacao);
    }

    private static string NormalizeSlug(string? value)
    {
        var slug = RequireText(value, "Slug da clinica obrigatorio", 120).ToLowerInvariant();
        if (!SlugPattern.IsMatch(slug))
        {
            throw new InvalidOperationException("Slug deve conter apenas letras minusculas, numeros e hifens");
        }

        return slug;
    }

    private static string RequireText(string? value, string message, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    private static string NormalizeOptional(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new InvalidOperationException($"Valor deve ter no maximo {maxLength} caracteres");
    }
}

public sealed record CreateClinicaRequest(
    string Nome,
    string Slug,
    string AdministradorNome,
    string AdministradorEmail,
    string AdministradorSenha,
    string? AdministradorTelefone,
    string? Plano,
    string? AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios);

public sealed record UpdateClinicaRequest(
    string? Nome,
    string? Slug,
    bool? Ativa,
    string? Plano,
    string? AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios);

public sealed record ClinicaPlatformResponse(
    int Id,
    string Nome,
    string Slug,
    bool Ativa,
    string Plano,
    string AssinaturaStatus,
    DateTime? TrialAte,
    DateTime? AssinaturaValidaAte,
    int? LimiteUsuarios,
    int? Usuarios,
    DateTime DataCadastro,
    DateTime? DataAtualizacao);
