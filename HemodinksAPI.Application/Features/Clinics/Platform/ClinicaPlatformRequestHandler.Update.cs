using System.Net.Mail;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Clinics.Platform;

public sealed partial class ClinicaPlatformRequestHandler
{
        public async Task<PlatformUseCaseResult> UpdateClinica(
        int id,
        UpdateClinicaRequest request,
        PlatformRequestContext requestContext,
        CancellationToken cancellationToken)
        {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
        throw new InvalidOperationException(validation.Errors[0].ErrorMessage);
        }

        var administratorNewPassword = string.IsNullOrWhiteSpace(request.AdministradorNovaSenha)
        ? null
        : RequireText(request.AdministradorNovaSenha, "Nova senha do administrador invalida", 200);
        if (administratorNewPassword is { Length: < 8 })
        {
        throw new InvalidOperationException("Nova senha do administrador deve possuir ao menos 8 caracteres");
        }
        
        var equipeNome = request.NovaEquipe == null
        ? null
        : RequireText(request.NovaEquipe.Nome, "Nome da equipe obrigatorio", 120);
        var equipeEmail = request.NovaEquipe == null
        ? null
        : GlobalIdentityService.NormalizeEmail(RequireText(request.NovaEquipe.Email, "Email da equipe obrigatorio", 255));
        var equipePassword = request.NovaEquipe == null
        ? null
        : RequireText(request.NovaEquipe.Senha, "Senha da equipe obrigatoria", 200);
        var equipeModo = request.NovaEquipe == null
        ? null
        : EquipeAuthenticationRules.NormalizeModo(request.NovaEquipe.ModoIdentificacao);
        
        if (equipeEmail != null
        && (!MailAddress.TryCreate(equipeEmail, out _) || equipePassword!.Length < 8))
        {
        throw new InvalidOperationException("Nova equipe deve possuir email valido e senha com ao menos 8 caracteres");
        }
        
        var clinica = await context.Clinicas.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (clinica == null)
        {
        return PlatformUseCaseResult.NotFound();
        }

        var updatesClinicRegistration = request.Cnpj is not null
            || request.Nome is not null
            || request.Slug is not null
            || request.Ativa.HasValue
            || request.Plano is not null
            || request.ModulosLiberados is not null
            || request.AssinaturaStatus is not null
            || request.TrialAte.HasValue
            || request.AssinaturaValidaAte.HasValue
            || request.LimiteUsuarios.HasValue
            || request.FotoClinica is not null;
        var resultingCnpj = request.Cnpj ?? clinica.Cnpj;
        if (updatesClinicRegistration && !CnpjUtils.IsValid(resultingCnpj))
        {
        throw new InvalidOperationException("Informe um CNPJ valido.");
        }

        var normalizedCnpj = updatesClinicRegistration
        ? CnpjUtils.Normalize(resultingCnpj)
        : null;
        if (normalizedCnpj != null
        && await context.Clinicas.AnyAsync(
        item => item.Id != id && item.Cnpj == normalizedCnpj,
        cancellationToken))
        {
        return PlatformUseCaseResult.Conflict(new { message = DuplicateCnpjMessage });
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
        return PlatformUseCaseResult.Conflict(new { message = "Slug da clinica ja cadastrado" });
        }
        
        clinica.Slug = slug;
        }

        if (request.Cnpj != null)
        {
        clinica.Cnpj = normalizedCnpj;
        }

        if (request.Ativa.HasValue) clinica.Ativa = request.Ativa.Value;
        var previousPlan = clinica.Plano;
        var nextPlan = request.Plano != null ? NormalizePlano(request.Plano) : previousPlan;
        clinica.Plano = nextPlan;
        if (nextPlan == ClinicaPlanos.Parcial)
        {
        clinica.ModulosLiberados = request.ModulosLiberados != null
        ? NormalizeModulos(nextPlan, request.ModulosLiberados)
        : previousPlan == ClinicaPlanos.Parcial
        ? NormalizeModulos(nextPlan, ClinicaModulos.Parse(clinica.ModulosLiberados))
        : throw new InvalidOperationException("Selecione ao menos um modulo para o plano Parcial");
        }
        else
        {
        clinica.ModulosLiberados = null;
        }
        if (request.AssinaturaStatus != null) clinica.AssinaturaStatus = NormalizeOptional(request.AssinaturaStatus, "Trial", 30);
        if (nextPlan == ClinicaPlanos.Trial)
        {
        clinica.TrialAte = request.TrialAte
        ?? (previousPlan == ClinicaPlanos.Trial ? null : DateTime.UtcNow.AddDays(14));
        clinica.AssinaturaValidaAte = null;
        }
        else
        {
        clinica.TrialAte = null;
        clinica.AssinaturaValidaAte = request.AssinaturaValidaAte;
        }
        if (request.LimiteUsuarios.HasValue)
        {
        if (request.LimiteUsuarios <= 0)
        {
        throw new InvalidOperationException("LimiteUsuarios deve ser maior que zero");
        }
        
        clinica.LimiteUsuarios = request.LimiteUsuarios;
        }
        
        if (request.Ativa.HasValue
        && request.Ativa.Value != clinica.Ativa
        && requestContext.PerfilId != Perfil.SuperAdministradorId)
        {
        return PlatformUseCaseResult.Forbidden();
        }
        
        if (equipeEmail != null
        && await context.UsuariosGlobais.AnyAsync(item => item.Email == equipeEmail, cancellationToken))
        {
        return PlatformUseCaseResult.Conflict(new { message = "Email coletivo ja utilizado por outra identidade" });
        }
        
        if (request.FotoClinica != null)
        {
        clinica.FotoClinica = await photoStorage.SaveAsync(
        request.FotoClinica,
        clinica.FotoClinica,
        cancellationToken);
        }
        clinica.DataAtualizacao = DateTime.UtcNow;
        
        var legacySettings = await context.ConfiguracoesSistema
        .FirstOrDefaultAsync(item => item.ClinicaId == clinica.Id, cancellationToken);
        if (legacySettings != null)
        {
        legacySettings.NomeEmpresa = clinica.Nome;
        legacySettings.FotoEmpresa = clinica.FotoClinica;
        legacySettings.DataAtualizacao = clinica.DataAtualizacao;
        }
        
        User? equipeLogin = null;
        Equipe? novaEquipe = null;
        User? clinicAdministrator = null;
        if (administratorNewPassword != null)
        {
        clinicAdministrator = await context.Users
        .Where(item => item.ClinicaId == id && item.PerfilId == Perfil.AdministradorId && item.Ativo)
        .OrderBy(item => item.DataCadastro)
        .ThenBy(item => item.Id)
        .FirstOrDefaultAsync(cancellationToken);
        if (clinicAdministrator == null)
        {
        return PlatformUseCaseResult.Conflict(new { message = "A clinica nao possui administrador ativo para redefinir a senha" });
        }
        
        clinicAdministrator.Senha = passwordHasher.HashPassword(administratorNewPassword);
        clinicAdministrator.PrecisaTrocarSenha = true;
        clinicAdministrator.DataAtualizacao = DateTime.UtcNow;
        }
        
        if (request.NovaEquipe != null)
        {
        equipeLogin = new User
        {
        ClinicaId = clinica.Id,
        Nome = equipeNome!,
        Email = equipeEmail!,
        Telefone = NormalizeOptional(request.NovaEquipe.Telefone, $"+558{DateTime.UtcNow.Ticks % 10_000_000_000:D10}", 20),
        Senha = passwordHasher.HashPassword(equipePassword!),
        DataCadastro = DateTime.UtcNow,
        Ativo = true,
        PrecisaTrocarSenha = false,
        PerfilId = Perfil.EquipeId
        };
        novaEquipe = new Equipe
        {
        ClinicaId = clinica.Id,
        Nome = equipeNome!,
        UsuarioLogin = equipeLogin,
        ModoIdentificacao = equipeModo!,
        Ativa = true,
        DataCadastro = DateTime.UtcNow
        };
        context.Users.Add(equipeLogin);
        context.Equipes.Add(novaEquipe);
        }
        
        try
        {
        await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsCnpjUniqueConstraintViolation(exception))
        {
        return PlatformUseCaseResult.Conflict(new { message = DuplicateCnpjMessage });
        }
        if (clinicAdministrator != null)
        {
        await GlobalIdentityService.SynchronizePasswordAsync(
        context,
        clinicAdministrator.Id,
        clinicAdministrator.Senha,
        cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
        requestContext,
        "clinic.administrator-password-reset",
        "user",
        clinicAdministrator.Id.ToString(),
        clinica.Id,
        new { clinicAdministrator.Email, RequiresPasswordChange = true },
        true,
        cancellationToken);
        }
        if (equipeLogin != null)
        {
        await GlobalIdentityService.EnsureForUserAsync(context, equipeLogin, cancellationToken);
        }
        await auditService.RecordAsync(
        requestContext,
        "clinic.update",
        "clinic",
        clinica.Id.ToString(),
        clinica.Id,
        new { clinica.Nome, clinica.Slug, clinica.Ativa, clinica.Plano, clinica.AssinaturaStatus },
        true,
        cancellationToken);
        if (novaEquipe != null)
        {
        await auditService.RecordAsync(
        requestContext,
        "team.create",
        "team",
        novaEquipe.Id.ToString(),
        clinica.Id,
        new { novaEquipe.Nome, novaEquipe.ModoIdentificacao, novaEquipe.UsuarioLoginId },
        true,
        cancellationToken);
        }
        
        return PlatformUseCaseResult.Ok(ToResponse(clinica, null));
        }
}
