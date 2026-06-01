using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Users.Queries;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Services;
using HemodinksAPI.Api.Storage;
using HemodinksAPI.Api.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        ILogger<CreateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _userPatientSyncService = userPatientSyncService;
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

            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);

            var user = new User
            {
                Nome = request.Nome,
                Email = request.Email,
                Telefone = request.Telefone,
                Cpf = cpf,
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
                FotoPerfil = user.FotoPerfil,
                DataCadastro = user.DataCadastro,
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
    private readonly ILogger<AuthenticateUserCommandHandler> _logger;

    public AuthenticateUserCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILogger<AuthenticateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
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

            _logger.LogInformation("Usuario autenticado com sucesso: {Email}", request.Email);

            return new AuthenticateUserResponse
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Token = token,
                Cpf = user.Cpf,
                FotoPerfil = user.FotoPerfil,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = UserProfileRules.GetPerfilNome(user)
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

            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, user.FotoPerfil, cancellationToken);

            user.Nome = request.Nome;
            user.Email = request.Email;
            user.Telefone = request.Telefone;
            user.Cpf = cpf;
            user.FotoPerfil = fotoPerfil;
            user.DataNascimento = request.DataNascimento;
            user.Ativo = request.Ativo;
            user.PerfilId = perfilId;

            await _userPatientSyncService.EnsurePacienteForUserAsync(user, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            return new UserDto
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Telefone = user.Telefone,
                Cpf = user.Cpf,
                FotoPerfil = user.FotoPerfil,
                DataCadastro = user.DataCadastro,
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
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        AppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _logger = logger;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Excluindo usuario: {UserId}", request.Id);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            var fotoPerfil = user.FotoPerfil;
            _context.Users.Remove(user);
            await _context.SaveChangesAsync(cancellationToken);
            await _profilePhotoStorage.DeleteAsync(fotoPerfil, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir usuario: {UserId}", request.Id);
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

internal static class UserProfileRules
{
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

public partial class ChangePasswordCommand : IRequest<ChangePasswordResponse>
{
}

public partial class ResetUserPasswordCommand : IRequest<ResetUserPasswordResponse>
{
}
