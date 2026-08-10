using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para criar novo usuario.
/// </summary>
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IUserPatientSyncService _userPatientSyncService;
    private readonly LicencaOptions _licencaOptions;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<CreateUserCommandHandler> _logger;
    private readonly IPasswordResetNotificationSender? _passwordResetNotificationSender;

    public CreateUserCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        IOptions<LicencaOptions> licencaOptions,
        ILogger<CreateUserCommandHandler> logger,
        IPasswordResetNotificationSender? passwordResetNotificationSender = null)
        : this(
            context,
            passwordHasher,
            profilePhotoStorage,
            userPatientSyncService,
            licencaOptions,
            ClinicaContextFactory.CreateDefaultResolved(),
            logger,
            passwordResetNotificationSender)
    {
    }

    public CreateUserCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        IUserPatientSyncService userPatientSyncService,
        IOptions<LicencaOptions> licencaOptions,
        IClinicaContext clinicaContext,
        ILogger<CreateUserCommandHandler> logger,
        IPasswordResetNotificationSender? passwordResetNotificationSender = null)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _userPatientSyncService = userPatientSyncService;
        _licencaOptions = licencaOptions.Value;
        _clinicaContext = clinicaContext;
        _logger = logger;
        _passwordResetNotificationSender = passwordResetNotificationSender;
    }

    public async Task<CreateUserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicaId = _clinicaContext.GetRequiredClinicaId();
            _logger.LogInformation("Criando novo usuario: {Email}", request.Email);

            var emailAlreadyExists = await _context.Users
                .AnyAsync(u => u.Email == request.Email, cancellationToken);

            if (emailAlreadyExists)
            {
                throw new InvalidOperationException("Email ja cadastrado");
            }

            var cpf = UserProfileRules.NormalizeCpf(request.Cpf);
            if (cpf != null)
            {
                var cpfAlreadyExists = await _context.Users
                    .AnyAsync(u => u.Cpf == cpf, cancellationToken);

                if (cpfAlreadyExists)
                {
                    throw new InvalidOperationException("CPF ja cadastrado");
                }
            }

            var perfilId = UserProfileRules.NormalizePerfilId(request.PerfilId);
            UserProfileRules.EnsureAssignablePerfilId(perfilId, request.CurrentUser);
            var perfil = await _context.Perfis
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == perfilId, cancellationToken);

            if (perfil == null)
            {
                throw new InvalidOperationException("Perfil invalido");
            }

            var medicalRegistration = UserProfileRules.NormalizeAndValidateMedicalRegistration(request.Crm, request.CrmUf, perfilId);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);
            var temporaryPassword = TemporaryPasswordGenerator.Generate();

            var user = new User
            {
                ClinicaId = clinicaId,
                Nome = request.Nome,
                Email = request.Email,
                Telefone = request.Telefone,
                Cpf = cpf,
                Crm = medicalRegistration.Crm,
                CrmUf = medicalRegistration.CrmUf,
                FotoPerfil = fotoPerfil,
                Senha = _passwordHasher.HashPassword(temporaryPassword),
                DataNascimento = request.DataNascimento,
                DataCadastro = DateTime.UtcNow,
                Ativo = true,
                PrecisaTrocarSenha = true,
                PerfilId = perfilId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);
            await GlobalIdentityService.EnsureForUserAsync(_context, user, cancellationToken);

            if (user.PerfilId == Perfil.MedicosId)
            {
                var now = DateTime.UtcNow;
                _context.Licencas.Add(new Licenca
                {
                    ClinicaId = clinicaId,
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
            var invitationSent = await FirstAccessInvitation.TrySendAsync(
                _context,
                _passwordResetNotificationSender,
                user,
                _logger,
                cancellationToken);

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
                PerfilNome = perfil.Nome,
                ConvitePrimeiroAcessoEnviado = invitationSent
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar usuario: {Email}", request.Email);
            throw;
        }
    }
}
