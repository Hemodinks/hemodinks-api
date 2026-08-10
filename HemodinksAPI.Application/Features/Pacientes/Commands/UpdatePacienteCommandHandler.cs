using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class UpdatePacienteCommandHandler : IRequestHandler<UpdatePacienteCommand, PacienteDto>
{
    private readonly IAppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<UpdatePacienteCommandHandler> _logger;

    public UpdatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<UpdatePacienteCommandHandler> logger)
        : this(
            context,
            cbhpmCache,
            profilePhotoStorage,
            ClinicaContextFactory.CreateDefaultResolved(),
            logger)
    {
    }

    public UpdatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IProfilePhotoStorage profilePhotoStorage,
        IClinicaContext clinicaContext,
        ILogger<UpdatePacienteCommandHandler> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
        _profilePhotoStorage = profilePhotoStorage;
        _clinicaContext = clinicaContext;
        _logger = logger;
    }

    public async Task<PacienteDto> Handle(UpdatePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _clinicaContext.GetRequiredClinicaId();
            PacienteRules.ValidateNome(request.NomePaciente);

            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .Include(p => p.FaturamentoMedico)
                .Include(p => p.Procedimentos)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            if (!await PacienteCommandAccess.CanEditPacienteAsync(_context, paciente, request.CurrentPerfilId, request.CurrentUserId, request.CurrentEquipeId, cancellationToken))
            {
                throw new UnauthorizedAccessException("Sem permissao para atualizar paciente");
            }

            var cpf = string.IsNullOrWhiteSpace(request.Cpf)
                ? paciente.User.Cpf
                : await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, paciente.UserId, cancellationToken);
            var email = string.IsNullOrWhiteSpace(request.Email)
                ? paciente.User.Email
                : await PacienteRules.ResolveEmailAsync(_context, request.Email, cpf, paciente.UserId, cancellationToken);
            var telefone = string.IsNullOrWhiteSpace(request.Telefone)
                ? paciente.User.Telefone
                : PacienteRules.ResolveTelefone(request.Telefone);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, paciente.User.FotoPerfil, cancellationToken);
            var medico = await PacienteRules.ResolveMedicoAsync(
                _context,
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentUserName,
                request.CurrentEquipeId,
                request.MedicoUserId,
                request.Medico,
                cancellationToken);
            var medicoAuxiliar1 = await PacienteRules.ResolveOptionalMedicoAsync(
                _context,
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentEquipeId,
                request.MedicoAuxiliar1UserId,
                request.MedicoAuxiliar1,
                cancellationToken);
            var medicoAuxiliar2 = await PacienteRules.ResolveOptionalMedicoAsync(
                _context,
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentEquipeId,
                request.MedicoAuxiliar2UserId,
                request.MedicoAuxiliar2,
                cancellationToken);
            PacienteRules.ValidateDistinctMedicos(medico, medicoAuxiliar1, medicoAuxiliar2);
            var hospital = request.HospitalId.HasValue || !string.IsNullOrWhiteSpace(request.Hospital)
                ? await PacienteRules.ResolveHospitalAsync(_context, request.HospitalId, request.Hospital, cancellationToken)
                : null;
            var convenio = await PacienteRules.ResolveConvenioAsync(_context, request.ConvenioId, request.Convenio, cancellationToken);
            var opmeFornecedor = await PacienteRules.ResolveOpmeFornecedorAsync(_context, request.OpmeFornecedorId, request.OpmeFornecedor, cancellationToken);
            var procedimentos = await PacienteRules.ResolveProcedimentosAsync(_cbhpmCache, request.Procedimentos,
                request.CbhpmCodigo, request.Procedimento, request.CbhpmPorte, cancellationToken);
            var procedimentoPrincipal = procedimentos.FirstOrDefault();
            paciente.User.Nome = request.NomePaciente.Trim();
            paciente.User.Email = email;
            paciente.User.Telefone = telefone;
            paciente.User.Cpf = cpf;
            paciente.User.FotoPerfil = fotoPerfil;
            paciente.User.DataNascimento = request.DataNascimento;
            paciente.User.Ativo = request.Ativo;
            paciente.User.PerfilId = Perfil.PacientesId;
            paciente.User.DataAtualizacao = DateTime.UtcNow;

            paciente.Data = request.Data;
            paciente.NomePaciente = paciente.User.Nome;
            paciente.Diagnostico = PacienteRules.TrimAndValidateOptional(request.Diagnostico, 100, "Diagnostico excede 100 caracteres");
            paciente.TratamentoMedico = PacienteRules.TrimAndValidateOptional(request.TratamentoMedico, 100, "Tratamento medico excede 100 caracteres");
            paciente.HospitalId = hospital?.Id;
            paciente.HospitalReferencia = hospital?.Referencia;
            paciente.Hospital = hospital?.Nome;
            paciente.MedicoUserId = medico.UserId;
            paciente.Medico = medico.Nome;
            paciente.MedicoAuxiliar1UserId = medicoAuxiliar1.UserId;
            paciente.MedicoAuxiliar1 = medicoAuxiliar1.Nome;
            paciente.MedicoAuxiliar2UserId = medicoAuxiliar2.UserId;
            paciente.MedicoAuxiliar2 = medicoAuxiliar2.Nome;
            paciente.ConvenioId = convenio?.Id;
            paciente.ConvenioReferencia = convenio?.Referencia;
            paciente.Convenio = convenio?.Descricao;
            paciente.OpmeFornecedorId = opmeFornecedor?.Id > 0 ? opmeFornecedor.Id : null;
            paciente.OpmeFornecedorReferencia = opmeFornecedor?.FornecedorReferencia;
            paciente.OpmeFornecedor = opmeFornecedor?.Fornecedor;
            paciente.CbhpmCodigo = procedimentoPrincipal?.Codigo;
            paciente.CbhpmPorte = procedimentoPrincipal?.Porte;
            paciente.Procedimento = procedimentoPrincipal?.Nome;
            paciente.Autorizacao = PacienteRules.TrimOptional(request.Autorizacao);
            paciente.Pagamento = PacienteRules.TrimOptional(request.Pagamento);
            paciente.RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa);
            paciente.StatusPago = request.StatusPago;
            paciente.Procedimentos.Clear();
            foreach (var procedimentoItem in PacienteRules.ToPacienteProcedimentos(procedimentos))
                paciente.Procedimentos.Add(procedimentoItem);
            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToDto(paciente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}
