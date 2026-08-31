using System.Net.Mail;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        public async Task<PlatformUseCaseResult> CreateClinica(
        CreateClinicaRequest request,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var nome = RequireText(request.Nome, "Nome da clinica obrigatorio", 120);
        var slug = NormalizeSlug(request.Slug);
        var adminNome = RequireText(request.AdministradorNome, "Nome do administrador obrigatorio", 255);
        var adminEmail = RequireText(request.AdministradorEmail, "Email do administrador obrigatorio", 255).ToLowerInvariant();
        var adminCredential = RequireText(request.AdministradorSenha, "Senha do administrador obrigatoria", 200);
        var equipeNome = request.EquipeInicial == null
        ? null
        : RequireText(request.EquipeInicial.Nome, "Nome da equipe obrigatorio", 120);
        var equipeEmail = request.EquipeInicial == null
        ? null
        : GlobalIdentityService.NormalizeEmail(RequireText(request.EquipeInicial.Email, "Email da equipe obrigatorio", 255));
        var equipeCredential = request.EquipeInicial == null
        ? null
        : RequireText(request.EquipeInicial.Senha, "Senha da equipe obrigatoria", 200);
        var equipeModo = request.EquipeInicial == null
        ? null
        : EquipeAuthenticationRules.NormalizeModo(request.EquipeInicial.ModoIdentificacao);
        
        if (!MailAddress.TryCreate(adminEmail, out _) || adminCredential.Length < 8)
        {
        throw new InvalidOperationException("Email invalido ou senha com menos de 8 caracteres");
        }
        
        if (equipeEmail != null
        && (!MailAddress.TryCreate(equipeEmail, out _)
        || equipeCredential!.Length < 8
        || equipeEmail.Equals(adminEmail, StringComparison.OrdinalIgnoreCase)))
        {
        throw new InvalidOperationException("Equipe inicial deve possuir email diferente do administrador e senha com ao menos 8 caracteres");
        }
        
        if (request.LimiteUsuarios is <= 0)
        {
        throw new InvalidOperationException("LimiteUsuarios deve ser maior que zero");
        }
        
        return await executionStrategy.ExecuteAsync(async operationCancellationToken =>
        {
        if (await context.Clinicas.AnyAsync(item => item.Slug == slug, operationCancellationToken))
        {
        return PlatformUseCaseResult.Conflict(new { message = "Slug da clinica ja cadastrado" });
        }
        
        if (equipeEmail != null
        && await context.UsuariosGlobais.AnyAsync(item => item.Email == equipeEmail, operationCancellationToken))
        {
        return PlatformUseCaseResult.Conflict(new { message = "Email coletivo ja utilizado por outra identidade" });
        }
        
        await using var transaction = await transactionManager.BeginAsync(operationCancellationToken);
        var now = DateTime.UtcNow;
        var plano = NormalizePlano(request.Plano);
        var clinica = new Clinica
        {
        Nome = nome,
        Slug = slug,
        Ativa = true,
        Plano = plano,
        ModulosLiberados = NormalizeModulos(plano, request.ModulosLiberados),
        AssinaturaStatus = NormalizeOptional(request.AssinaturaStatus, "Trial", 30),
        TrialAte = plano == ClinicaPlanos.Trial ? request.TrialAte ?? now.AddDays(14) : null,
        AssinaturaValidaAte = plano == ClinicaPlanos.Trial ? null : request.AssinaturaValidaAte,
        LimiteUsuarios = request.LimiteUsuarios,
        DataCadastro = now
        };
        
        context.Clinicas.Add(clinica);
        await context.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.FotoClinica))
        {
        clinica.FotoClinica = await photoStorage.SaveAsync(request.FotoClinica, null, cancellationToken);
        }
        
        var admin = new User
        {
        ClinicaId = clinica.Id,
        Nome = adminNome,
        Email = adminEmail,
        Telefone = NormalizeOptional(request.AdministradorTelefone, $"+550{clinica.Id:00000000000}", 20),
        Senha = passwordHasher.HashPassword(adminCredential),
        DataCadastro = now,
        Ativo = true,
        PrecisaTrocarSenha = true,
        PerfilId = Perfil.AdministradorId
        };
        
        context.Users.Add(admin);
        User? equipeLogin = null;
        Equipe? equipeInicial = null;
        if (request.EquipeInicial != null)
        {
        equipeLogin = new User
        {
        ClinicaId = clinica.Id,
        Nome = equipeNome!,
        Email = equipeEmail!,
        Telefone = NormalizeOptional(request.EquipeInicial.Telefone, $"+558{DateTime.UtcNow.Ticks % 10_000_000_000:D10}", 20),
        Senha = passwordHasher.HashPassword(equipeCredential!),
        DataCadastro = now,
        Ativo = true,
        PrecisaTrocarSenha = false,
        PerfilId = Perfil.EquipeId
        };
        equipeInicial = new Equipe
        {
        ClinicaId = clinica.Id,
        Nome = equipeNome!,
        UsuarioLogin = equipeLogin,
        ModoIdentificacao = equipeModo!,
        Ativa = true,
        DataCadastro = now
        };
        context.Users.Add(equipeLogin);
        context.Equipes.Add(equipeInicial);
        }
        context.ConfiguracoesSistema.Add(new ConfiguracaoSistema
        {
        ClinicaId = clinica.Id,
        NomeEmpresa = clinica.Nome,
        FotoEmpresa = clinica.FotoClinica
        });
        
        var platformShadowUser = await AddPlatformShadowUserAsync(requestContext, clinica.Id, context, cancellationToken);
        await CloneClinicReferenceDataAsync(clinica.Id, context, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await GlobalIdentityService.EnsureForUserAsync(context, admin, cancellationToken, clinicaPadrao: true);
        if (equipeLogin != null)
        {
        await GlobalIdentityService.EnsureForUserAsync(context, equipeLogin, cancellationToken);
        }
        if (platformShadowUser != null)
        {
        await GlobalIdentityService.EnsureForUserAsync(context, platformShadowUser, cancellationToken);
        }
        
        await auditService.RecordAsync(
        requestContext,
        "clinic.create",
        "clinic",
        clinica.Id.ToString(),
        clinica.Id,
        new { clinica.Nome, clinica.Slug, admin.Email },
        true,
        cancellationToken);
        
        if (equipeInicial != null)
        {
        await auditService.RecordAsync(
        requestContext,
        "team.create",
        "team",
        equipeInicial.Id.ToString(),
        clinica.Id,
        new { equipeInicial.Nome, equipeInicial.ModoIdentificacao, equipeInicial.UsuarioLoginId },
        true,
        cancellationToken);
        }
        
        if (transaction != null)
        {
        await transaction.CommitAsync(operationCancellationToken);
        }
        
        var userCount = await ClinicEmployees(context)
        .CountAsync(item => item.ClinicaId == clinica.Id, cancellationToken);
        return PlatformUseCaseResult.Created($"/api/platform/clinicas/{clinica.Id}", ToResponse(clinica, userCount));
        }, cancellationToken);
        }

        private static async Task<User?> AddPlatformShadowUserAsync(
        PlatformRequestContext requestContext,
        int clinicaId,
        IPlatformClinicDbContext context,
        CancellationToken cancellationToken)
        {
        var sourceId = requestContext.UserId.GetValueOrDefault();
        var source = await context.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == sourceId && item.PerfilId == Perfil.SuperAdministradorId, cancellationToken);
        
        if (source == null || await context.Users.AnyAsync(
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

        private static async Task CloneClinicReferenceDataAsync(
        int targetClinicaId,
        IPlatformClinicDbContext context,
        CancellationToken cancellationToken)
        {
        var convenios = await context.Convenios.AsNoTracking()
        .Where(item => item.ClinicaId == Clinica.DefaultId)
        .Select(item => item.DescricaoConvenio)
        .ToListAsync(cancellationToken);
        context.Convenios.AddRange(convenios.Select(descricao => new Convenio
        {
        ClinicaId = targetClinicaId,
        DescricaoConvenio = descricao
        }));
        
        var hospitais = await context.Hospitais.AsNoTracking()
        .Where(item => item.ClinicaId == Clinica.DefaultId)
        .Select(item => item.Nome)
        .ToListAsync(cancellationToken);
        context.Hospitais.AddRange(hospitais.Select(nome => new Hospital { ClinicaId = targetClinicaId, Nome = nome }));
        
        var fornecedores = await context.OPME.AsNoTracking()
        .Where(item => item.ClinicaId == Clinica.DefaultId)
        .Select(item => item.Fornecedor)
        .ToListAsync(cancellationToken);
        context.OPME.AddRange(fornecedores.Select(nome => new Domain.Models.Opme { ClinicaId = targetClinicaId, Fornecedor = nome }));
        }
}
