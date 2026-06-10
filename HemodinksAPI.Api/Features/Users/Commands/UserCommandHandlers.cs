using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Licencas;
using HemodinksAPI.Api.Features.Users.Queries;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Services;
using HemodinksAPI.Api.Storage;
using HemodinksAPI.Api.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Api.Features.Users.Commands;

/// <summary>
/// Handler para criar novo usuario.
/// </summary>
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IUserPatientSyncService _userPatientSyncService;
    private readonly LicencaOptions _licencaOptions;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        IOptions<LicencaOptions> licencaOptions,
        ILogger<CreateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _userPatientSyncService = userPatientSyncService;
        _licencaOptions = licencaOptions.Value;
        _logger = logger;
    }

    public async Task<CreateUserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Criando novo usuario: {Email}", request.Email);

            var emailAlreadyExists = await _context.Users
                .AnyAsync(u => u.Email == request.Email, cancellationToken);

            if (emailAlreadyExists)
            {
                throw new InvalidOperationException("Email ja cadastrado");
            }

            var cpf = UserProfileRules.NormalizeAndValidateCpf(request.Cpf);
            var cpfAlreadyExists = await _context.Users
                .AnyAsync(u => u.Cpf == cpf, cancellationToken);

            if (cpfAlreadyExists)
            {
                throw new InvalidOperationException("CPF ja cadastrado");
            }

            var perfilId = UserProfileRules.NormalizePerfilId(request.PerfilId);
            var perfil = await _context.Perfis
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == perfilId, cancellationToken);

            if (perfil == null)
            {
                throw new InvalidOperationException("Perfil invalido");
            }

            var medicalRegistration = UserProfileRules.NormalizeAndValidateMedicalRegistration(request.Crm, request.CrmUf, perfilId);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);

            var user = new User
            {
                Nome = request.Nome,
                Email = request.Email,
                Telefone = request.Telefone,
                Cpf = cpf,
                Crm = medicalRegistration.Crm,
                CrmUf = medicalRegistration.CrmUf,
                FotoPerfil = fotoPerfil,
                Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value),
                DataNascimento = request.DataNascimento,
                DataCadastro = DateTime.UtcNow,
                Ativo = true,
                PrecisaTrocarSenha = true,
                PerfilId = perfilId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            if (user.PerfilId == Perfil.MedicosId)
            {
                var now = DateTime.UtcNow;
                _context.Licencas.Add(new Licenca
                {
                    UserId = user.Id,
                    Plano = LicencaPlanos.Trial,
                    Status = LicencaStatus.Ativa,
                    DataInicioTrial = now,
                    DataFimTrial = now.AddDays(Math.Max(1, _licencaOptions.TrialDays)),
                    DataCadastro = now
                });
            }

            await _userPatientSyncService.EnsurePacienteForUserAsync(user, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Usuario criado com sucesso. ID: {UserId}", user.Id);

            return new CreateUserResponse
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Telefone = user.Telefone,
                Cpf = user.Cpf,
                Crm = user.Crm,
                CrmUf = user.CrmUf,
                FotoPerfil = user.FotoPerfil,
                DataCadastro = user.DataCadastro,
                DataAtualizacao = user.DataAtualizacao,
                DataNascimento = user.DataNascimento,
                Ativo = user.Ativo,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = perfil.Nome
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar usuario: {Email}", request.Email);
            throw;
        }
    }
}

/// <summary>
/// Handler para autenticar usuario.
/// </summary>
public class AuthenticateUserCommandHandler : IRequestHandler<AuthenticateUserCommand, AuthenticateUserResponse>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILicencaService _licencaService;
    private readonly ILogger<AuthenticateUserCommandHandler> _logger;

    public AuthenticateUserCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        ILogger<AuthenticateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _licencaService = licencaService;
        _logger = logger;
    }

    public async Task<AuthenticateUserResponse> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Autenticando usuario: {Email}", request.Email);

            var user = await _context.Users
                .Include(u => u.Perfil)
                .FirstOrDefaultAsync(u => u.Email == request.Email && u.Ativo, cancellationToken);

            if (user == null || !_passwordHasher.VerifyPassword(request.Senha, user.Senha))
            {
                _logger.LogWarning("Falha na autenticacao para: {Email}", request.Email);
                throw new UnauthorizedAccessException("Email ou senha invalidos");
            }

            var token = _jwtTokenService.GenerateToken(user);
            var licenca = user.PerfilId == Perfil.MedicosId
                ? await _licencaService.GetOrCreateForMedicoAsync(user.Id, cancellationToken)
                : null;

            _logger.LogInformation("Usuario autenticado com sucesso: {Email}", request.Email);

            return new AuthenticateUserResponse
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Token = token,
                Cpf = user.Cpf,
                Crm = user.Crm,
                CrmUf = user.CrmUf,
                FotoPerfil = user.FotoPerfil,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = UserProfileRules.GetPerfilNome(user),
                Licenca = licenca
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar usuario: {Email}", request.Email);
            throw;
        }
    }
}

/// <summary>
/// Handler para atualizar usuario.
/// </summary>
public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
    private readonly AppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IUserPatientSyncService _userPatientSyncService;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        AppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _userPatientSyncService = userPatientSyncService;
        _logger = logger;
    }

    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Atualizando usuario: {UserId}", request.Id);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            var emailAlreadyExists = await _context.Users
                .AnyAsync(u => u.Id != request.Id && u.Email == request.Email, cancellationToken);

            if (emailAlreadyExists)
            {
                throw new InvalidOperationException("Email ja cadastrado");
            }

            var cpf = UserProfileRules.NormalizeAndValidateCpf(request.Cpf);
            var cpfAlreadyExists = await _context.Users
                .AnyAsync(u => u.Id != request.Id && u.Cpf == cpf, cancellationToken);

            if (cpfAlreadyExists)
            {
                throw new InvalidOperationException("CPF ja cadastrado");
            }

            var perfilId = UserProfileRules.NormalizePerfilId(request.PerfilId);
            var perfil = await _context.Perfis
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == perfilId, cancellationToken);

            if (perfil == null)
            {
                throw new InvalidOperationException("Perfil invalido");
            }

            var medicalRegistration = UserProfileRules.NormalizeAndValidateMedicalRegistration(request.Crm, request.CrmUf, perfilId);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, user.FotoPerfil, cancellationToken);

            user.Nome = request.Nome;
            user.Email = request.Email;
            user.Telefone = request.Telefone;
            user.Cpf = cpf;
            user.Crm = medicalRegistration.Crm;
            user.CrmUf = medicalRegistration.CrmUf;
            user.FotoPerfil = fotoPerfil;
            user.DataNascimento = request.DataNascimento;
            user.Ativo = request.Ativo;
            user.PerfilId = perfilId;
            user.DataAtualizacao = DateTime.UtcNow;

            await _userPatientSyncService.EnsurePacienteForUserAsync(user, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return new UserDto
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Telefone = user.Telefone,
                Cpf = user.Cpf,
                Crm = user.Crm,
                CrmUf = user.CrmUf,
                FotoPerfil = user.FotoPerfil,
                DataCadastro = user.DataCadastro,
                DataAtualizacao = user.DataAtualizacao,
                DataNascimento = user.DataNascimento,
                Ativo = user.Ativo,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = perfil.Nome
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar usuario: {UserId}", request.Id);
            throw;
        }
    }
}

/// <summary>
/// Handler para excluir usuario.
/// </summary>
public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly AppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        AppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IPatientFileStorage patientFileStorage,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Excluindo usuario: {UserId}", request.Id);

            var user = await _context.Users
                .Include(u => u.Arquivos)
                .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            var fotoPerfil = user.FotoPerfil;
            var fileUrls = user.Arquivos.Select(arquivo => arquivo.Url).ToList();
            _context.Users.Remove(user);
            await _context.SaveChangesAsync(cancellationToken);
            await _profilePhotoStorage.DeleteAsync(fotoPerfil, cancellationToken);

            foreach (var fileUrl in fileUrls)
            {
                await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir usuario: {UserId}", request.Id);
            throw;
        }
    }
}

public class UploadUserArquivoCommandHandler : IRequestHandler<UploadUserArquivoCommand, UserArquivoDto>
{
    private readonly AppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<UploadUserArquivoCommandHandler> _logger;

    public UploadUserArquivoCommandHandler(
        AppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<UploadUserArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task<UserArquivoDto> Handle(UploadUserArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            if (user.PerfilId != Perfil.MedicosId)
            {
                throw new InvalidOperationException("Documentos de cadastro estao disponiveis apenas para medicos");
            }

            var storedFile = await _patientFileStorage.SaveAsync(request.File, cancellationToken);
            var arquivo = new UserArquivo
            {
                UserId = request.UserId,
                NomeOriginal = storedFile.OriginalName,
                ContentType = storedFile.ContentType,
                TamanhoBytes = storedFile.SizeBytes,
                Url = storedFile.Url,
                DataUpload = DateTime.UtcNow
            };

            user.DataAtualizacao = DateTime.UtcNow;
            _context.UserArquivos.Add(arquivo);
            await _context.SaveChangesAsync(cancellationToken);

            return UserMapper.ToArquivoDto(arquivo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar arquivo do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

public class DeleteUserArquivoCommandHandler : IRequestHandler<DeleteUserArquivoCommand>
{
    private readonly AppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeleteUserArquivoCommandHandler> _logger;

    public DeleteUserArquivoCommandHandler(
        AppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<DeleteUserArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeleteUserArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var arquivo = await _context.UserArquivos
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == request.ArquivoId && a.UserId == request.UserId, cancellationToken);

            if (arquivo == null)
            {
                throw new KeyNotFoundException("Arquivo nao encontrado");
            }

            var fileUrl = arquivo.Url;
            arquivo.User.DataAtualizacao = DateTime.UtcNow;
            _context.UserArquivos.Remove(arquivo);
            await _context.SaveChangesAsync(cancellationToken);
            await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir arquivo {ArquivoId} do usuario {UserId}", request.ArquivoId, request.UserId);
            throw;
        }
    }
}

/// <summary>
/// Handler para trocar senha do usuario autenticado.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, ChangePasswordResponse>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ChangePasswordResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Alterando senha do usuario: {UserId}", request.UserId);

            if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
            {
                throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
            }

            if (request.NovaSenha == DefaultUserPassword.Value)
            {
                throw new InvalidOperationException("A nova senha nao pode ser a senha padrao");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Ativo, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            if (!_passwordHasher.VerifyPassword(request.SenhaAtual, user.Senha))
            {
                throw new UnauthorizedAccessException("Senha atual invalida");
            }

            if (_passwordHasher.VerifyPassword(request.NovaSenha, user.Senha))
            {
                throw new InvalidOperationException("A nova senha nao pode ser igual a senha atual");
            }

            user.Senha = _passwordHasher.HashPassword(request.NovaSenha);
            user.PrecisaTrocarSenha = false;
            user.DataAtualizacao = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new ChangePasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha alterada com sucesso"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar senha do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

/// <summary>
/// Handler para resetar a senha do usuario para a senha padrao.
/// </summary>
public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, ResetUserPasswordResponse>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    public ResetUserPasswordCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ResetUserPasswordCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ResetUserPasswordResponse> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Resetando senha do usuario: {UserId}", request.UserId);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            user.Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value);
            user.PrecisaTrocarSenha = true;
            user.DataAtualizacao = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new ResetUserPasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha resetada para a senha padrao"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao resetar senha do usuario: {UserId}", request.UserId);
            throw;
        }
    }
}

/// <summary>
/// Handler para resetar a senha do usuario pelo email.
/// </summary>
public class ResetUserPasswordByEmailCommandHandler : IRequestHandler<ResetUserPasswordByEmailCommand, ResetUserPasswordResponse>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetUserPasswordByEmailCommandHandler> _logger;

    public ResetUserPasswordByEmailCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ResetUserPasswordByEmailCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ResetUserPasswordResponse> Handle(ResetUserPasswordByEmailCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var email = request.Email.Trim();
            _logger.LogInformation("Resetando senha do usuario pelo email: {Email}", email);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.Ativo, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            user.Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value);
            user.PrecisaTrocarSenha = true;
            user.DataAtualizacao = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new ResetUserPasswordResponse
            {
                Id = user.Id,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                Message = "Senha resetada para a senha padrao"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao resetar senha pelo email: {Email}", request.Email);
            throw;
        }
    }
}

internal static class UserProfileRules
{
    private const int MaxCrmLength = 20;

    private static readonly HashSet<string> ValidBrazilUf = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
        "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    public static int NormalizePerfilId(int perfilId)
    {
        return perfilId == 0 ? Perfil.MedicosId : perfilId;
    }

    public static string GetPerfilNome(User user)
    {
        return user.Perfil?.Nome ?? string.Empty;
    }

    public static string NormalizeAndValidateCpf(string? cpf)
    {
        if (!CpfUtils.IsValid(cpf))
        {
            throw new InvalidOperationException("CPF invalido");
        }

        return CpfUtils.Normalize(cpf)!;
    }

    public static (string? Crm, string? CrmUf) NormalizeAndValidateMedicalRegistration(
        string? crm,
        string? crmUf,
        int perfilId)
    {
        if (perfilId != Perfil.MedicosId)
        {
            return (null, null);
        }

        var normalizedCrm = crm?.Trim();
        var normalizedCrmUf = crmUf?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCrm))
        {
            throw new InvalidOperationException("CRM obrigatorio para medicos");
        }

        if (normalizedCrm.Length > MaxCrmLength)
        {
            throw new InvalidOperationException($"CRM deve ter no maximo {MaxCrmLength} caracteres");
        }

        if (string.IsNullOrWhiteSpace(normalizedCrmUf))
        {
            throw new InvalidOperationException("UF do CRM obrigatoria para medicos");
        }

        if (!ValidBrazilUf.Contains(normalizedCrmUf))
        {
            throw new InvalidOperationException("UF do CRM invalida");
        }

        return (normalizedCrm, normalizedCrmUf);
    }
}

// Implementar MediatR IRequest para os comandos.
public partial class CreateUserCommand : IRequest<CreateUserResponse>
{
}

public partial class AuthenticateUserCommand : IRequest<AuthenticateUserResponse>
{
}

public partial class UpdateUserCommand : IRequest<UserDto>
{
}

public partial class DeleteUserCommand : IRequest
{
}

public partial class UploadUserArquivoCommand : IRequest<UserArquivoDto>
{
}

public partial class DeleteUserArquivoCommand : IRequest
{
}

public partial class ChangePasswordCommand : IRequest<ChangePasswordResponse>
{
}

public partial class ResetUserPasswordCommand : IRequest<ResetUserPasswordResponse>
{
}

public partial class ResetUserPasswordByEmailCommand : IRequest<ResetUserPasswordResponse>
{
}
