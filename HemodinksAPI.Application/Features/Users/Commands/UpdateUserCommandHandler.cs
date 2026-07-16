using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para atualizar usuario.
/// </summary>
public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
    private readonly IAppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IUserPatientSyncService _userPatientSyncService;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IAppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        ILogger<UpdateUserCommandHandler> logger)
        : this(
            context,
            profilePhotoStorage,
            userPatientSyncService,
            ClinicaContextFactory.CreateDefaultResolved(),
            logger)
    {
    }

    public UpdateUserCommandHandler(
        IAppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        IClinicaContext clinicaContext,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _userPatientSyncService = userPatientSyncService;
        _clinicaContext = clinicaContext;
        _logger = logger;
    }

    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _clinicaContext.GetRequiredClinicaId();
            _logger.LogInformation("Atualizando usuario: {UserId}", request.Id);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException("Usuario nao encontrado");
            }

            var effectivePerfilId = request.PerfilId;
            var effectiveAtivo = request.Ativo;

            if (request.CurrentUser != null && !UserCommandAccess.CanUpdateUser(request.CurrentUser, request.Id))
            {
                throw new UnauthorizedAccessException("Sem permissao para atualizar usuario");
            }

            if (request.CurrentUser != null && !request.CurrentUser.IsAdministrador)
            {
                effectivePerfilId = Perfil.MedicosId;
                effectiveAtivo = true;
            }

            var emailAlreadyExists = await _context.Users
                .AnyAsync(u => u.Id != request.Id && u.Email == request.Email, cancellationToken);

            if (emailAlreadyExists)
            {
                throw new InvalidOperationException("Email ja cadastrado");
            }

            var cpf = UserProfileRules.NormalizeAndValidateCpf(request.Cpf);
            if (cpf != null)
            {
                var cpfAlreadyExists = await _context.Users
                    .AnyAsync(u => u.Id != request.Id && u.Cpf == cpf, cancellationToken);

                if (cpfAlreadyExists)
                {
                    throw new InvalidOperationException("CPF ja cadastrado");
                }
            }

            var perfilId = UserProfileRules.NormalizePerfilId(effectivePerfilId);
            UserProfileRules.EnsureAssignablePerfilId(perfilId);
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
            user.Ativo = effectiveAtivo;
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
