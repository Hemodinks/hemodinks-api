using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using MediatR;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class CreatePacienteCommandHandler : IRequestHandler<CreatePacienteCommand, PacienteDto>
{
    private readonly IAppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<CreatePacienteCommandHandler> _logger;

    public CreatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<CreatePacienteCommandHandler> logger)
        : this(
            context,
            cbhpmCache,
            passwordHasher,
            profilePhotoStorage,
            ClinicaContextFactory.CreateDefaultResolved(),
            logger)
    {
    }

    public CreatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        IClinicaContext clinicaContext,
        ILogger<CreatePacienteCommandHandler> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _clinicaContext = clinicaContext;
        _logger = logger;
    }

    public async Task<PacienteDto> Handle(CreatePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicaId = _clinicaContext.GetRequiredClinicaId();
            if (!PacienteCommandAccess.CanCreate(request.CurrentPerfilId))
            {
                throw new UnauthorizedAccessException("Sem permissao para criar paciente");
            }

            PacienteRules.ValidateNome(request.NomePaciente);
            var diagnostico = PacienteRules.TrimAndValidateOptional(request.Diagnostico, 100, "Diagnostico excede 100 caracteres");
            var tratamentoMedico = PacienteRules.TrimAndValidateOptional(request.TratamentoMedico, 100, "Tratamento medico excede 100 caracteres");
            var cpf = await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, null, cancellationToken);
            var email = await PacienteRules.ResolveEmailAsync(_context, request.Email, cpf, null, cancellationToken);
            var telefone = PacienteRules.ResolveTelefone(request.Telefone);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);
            var medico = await PacienteRules.ResolveMedicoAsync(_context, request.CurrentPerfilId, request.CurrentUserId,
                request.CurrentUserName, request.MedicoUserId, request.Medico, cancellationToken);
            var medicoAuxiliar1 = await PacienteRules.ResolveOptionalMedicoAsync(_context, request.CurrentPerfilId,
                request.CurrentUserId, request.MedicoAuxiliar1UserId, request.MedicoAuxiliar1, cancellationToken);
            var medicoAuxiliar2 = await PacienteRules.ResolveOptionalMedicoAsync(_context, request.CurrentPerfilId,
                request.CurrentUserId, request.MedicoAuxiliar2UserId, request.MedicoAuxiliar2, cancellationToken);
            PacienteRules.ValidateDistinctMedicos(medico, medicoAuxiliar1, medicoAuxiliar2);
            var hospital = request.HospitalId.HasValue || !string.IsNullOrWhiteSpace(request.Hospital)
                ? await PacienteRules.ResolveHospitalAsync(_context, request.HospitalId, request.Hospital, cancellationToken)
                : null;
            var convenio = await PacienteRules.ResolveConvenioAsync(_context, request.ConvenioId, request.Convenio, cancellationToken);
            var opmeFornecedor = await PacienteRules.ResolveOpmeFornecedorAsync(_context, request.OpmeFornecedorId, request.OpmeFornecedor, cancellationToken);
            var procedimentos = await PacienteRules.ResolveProcedimentosAsync(_cbhpmCache, request.Procedimentos,
                request.CbhpmCodigo, request.Procedimento, request.CbhpmPorte, cancellationToken);
            var procedimentoPrincipal = procedimentos.FirstOrDefault();
            var user = new User
            {
                ClinicaId = clinicaId,
                Nome = request.NomePaciente.Trim(),
                Email = email,
                Telefone = telefone,
                Cpf = cpf,
                FotoPerfil = fotoPerfil,
                Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value),
                DataCadastro = DateTime.UtcNow,
                DataNascimento = request.DataNascimento,
                Ativo = request.Ativo,
                PrecisaTrocarSenha = true,
                PerfilId = Perfil.PacientesId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);
            await GlobalIdentityService.EnsureForUserAsync(_context, user, cancellationToken);

            var paciente = new Paciente
            {
                ClinicaId = clinicaId,
                UserId = user.Id,
                User = user,
                Data = request.Data,
                NomePaciente = user.Nome,
                Diagnostico = diagnostico,
                TratamentoMedico = tratamentoMedico,
                HospitalId = hospital?.Id,
                HospitalReferencia = hospital?.Referencia,
                Hospital = hospital?.Nome,
                MedicoUserId = medico.UserId,
                Medico = medico.Nome,
                MedicoAuxiliar1UserId = medicoAuxiliar1.UserId,
                MedicoAuxiliar1 = medicoAuxiliar1.Nome,
                MedicoAuxiliar2UserId = medicoAuxiliar2.UserId,
                MedicoAuxiliar2 = medicoAuxiliar2.Nome,
                ConvenioId = convenio?.Id,
                ConvenioReferencia = convenio?.Referencia,
                Convenio = convenio?.Descricao,
                OpmeFornecedorId = opmeFornecedor?.Id > 0 ? opmeFornecedor.Id : null,
                OpmeFornecedorReferencia = opmeFornecedor?.FornecedorReferencia,
                OpmeFornecedor = opmeFornecedor?.Fornecedor,
                CbhpmCodigo = procedimentoPrincipal?.Codigo,
                CbhpmPorte = procedimentoPrincipal?.Porte,
                Procedimento = procedimentoPrincipal?.Nome,
                Autorizacao = PacienteRules.TrimOptional(request.Autorizacao),
                Pagamento = PacienteRules.TrimOptional(request.Pagamento),
                RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa),
                StatusPago = request.StatusPago,
                Procedimentos = PacienteRules.ToPacienteProcedimentos(procedimentos)
            };

            _context.Pacientes.Add(paciente);
            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToDto(paciente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar paciente: {NomePaciente}", request.NomePaciente);
            throw;
        }
    }
}
