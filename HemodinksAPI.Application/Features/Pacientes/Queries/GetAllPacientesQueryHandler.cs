using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Queries;

public class GetAllPacientesQueryHandler : IRequestHandler<GetAllPacientesQuery, PagedResult<PacienteDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllPacientesQueryHandler> _logger;

    public GetAllPacientesQueryHandler(IAppDbContext context, ILogger<GetAllPacientesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<PacienteDto>> Handle(GetAllPacientesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());
            var canUseAdminFilters = request.CurrentPerfilId == Perfil.AdministradorId;
            var medico = canUseAdminFilters ? PacienteQueryOrdering.TrimOptional(request.Medico) : null;
            var convenio = canUseAdminFilters ? PacienteQueryOrdering.TrimOptional(request.Convenio) : null;
            var procedimento = canUseAdminFilters ? PacienteQueryOrdering.TrimOptional(request.Procedimento) : null;

            var query = _context.Pacientes.AsNoTracking();
            query = PacienteAccess.ApplyScope(_context, query, request.CurrentPerfilId, request.CurrentUserId);
            query = ApplyFilters(query, search, digits, medico, convenio, procedimento);

            var totalItems = await query.CountAsync(cancellationToken);
            query = PacienteQueryOrdering.ApplyOrdering(query, request.SortBy, request.SortDirection);

            var pacientes = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PacienteDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    Data = p.Data,
                    DataCadastro = p.User.DataCadastro,
                    DataAtualizacao = p.User.DataAtualizacao,
                    NomePaciente = p.NomePaciente,
                    Diagnostico = p.Diagnostico,
                    HospitalId = p.HospitalId,
                    Hospital = p.HospitalReferencia != null ? p.HospitalReferencia.Nome : p.Hospital,
                    MedicoUserId = p.MedicoUserId,
                    Medico = p.MedicoUser != null ? p.MedicoUser.Nome : p.Medico,
                    MedicoAuxiliar1UserId = p.MedicoAuxiliar1UserId,
                    MedicoAuxiliar1 = p.MedicoAuxiliar1User != null ? p.MedicoAuxiliar1User.Nome : p.MedicoAuxiliar1,
                    MedicoAuxiliar2UserId = p.MedicoAuxiliar2UserId,
                    MedicoAuxiliar2 = p.MedicoAuxiliar2User != null ? p.MedicoAuxiliar2User.Nome : p.MedicoAuxiliar2,
                    ConvenioId = p.ConvenioId,
                    Convenio = p.ConvenioReferencia != null ? p.ConvenioReferencia.DescricaoConvenio : p.Convenio,
                    OpmeFornecedorId = p.OpmeFornecedorId,
                    OpmeFornecedor = p.OpmeFornecedorReferencia != null ? p.OpmeFornecedorReferencia.Fornecedor : p.OpmeFornecedor,
                    CbhpmCodigo = p.CbhpmCodigo,
                    CbhpmPorte = p.CbhpmPorte,
                    Procedimento = p.Procedimento,
                    Procedimentos = p.Procedimentos
                        .OrderBy(item => item.Ordem)
                        .ThenBy(item => item.Id)
                        .Select(item => new PacienteProcedimentoDto
                        {
                            Id = item.Id,
                            CbhpmCodigo = item.CbhpmCodigo,
                            CbhpmPorte = item.CbhpmPorte,
                            Procedimento = item.Procedimento,
                            ValorReferencia = item.ValorReferencia,
                            Ordem = item.Ordem
                        })
                        .ToList(),
                    Autorizacao = p.Autorizacao,
                    Pagamento = p.Pagamento,
                    RepasseGlosa = p.RepasseGlosa,
                    StatusPago = p.StatusPago,
                    Cpf = p.User.Cpf,
                    Email = p.User.Email,
                    Telefone = p.User.Telefone,
                    FotoPerfil = p.User.FotoPerfil,
                    DataNascimento = p.User.DataNascimento,
                    Ativo = p.User.Ativo,
                    ArquivosCount = p.Arquivos.Count,
                    Faturamento = p.FaturamentoMedico == null ? null : new PacienteFaturamentoDto
                    {
                        Id = p.FaturamentoMedico.Id,
                        PacienteId = p.FaturamentoMedico.PacienteId,
                        HonorariosCirurgiao = p.FaturamentoMedico.HonorariosCirurgiao,
                        HonorariosAuxiliares = p.FaturamentoMedico.HonorariosAuxiliares,
                        HonorariosAnestesista = p.FaturamentoMedico.HonorariosAnestesista,
                        AnestesistaFaturadoSeparado = p.FaturamentoMedico.AnestesistaFaturadoSeparado,
                        Anestesista = p.FaturamentoMedico.Anestesista,
                        CodigoTussCbhpmAmb = p.FaturamentoMedico.CodigoTussCbhpmAmb,
                        PorteCirurgicoAnestesico = p.FaturamentoMedico.PorteCirurgicoAnestesico,
                        GuiaAutorizacaoConvenio = p.FaturamentoMedico.GuiaAutorizacaoConvenio,
                        GuiaInternacaoOuSadt = p.FaturamentoMedico.GuiaInternacaoOuSadt,
                        OpmeMateriaisEspeciais = p.FaturamentoMedico.OpmeMateriaisEspeciais,
                        TissXmlStatus = p.FaturamentoMedico.TissXmlStatus,
                        ValorGlosa = p.FaturamentoMedico.ValorGlosa,
                        GlosaStatus = p.FaturamentoMedico.GlosaStatus,
                        RecursoGlosa = p.FaturamentoMedico.RecursoGlosa,
                        ConferenciaPagamentoRealizada = p.FaturamentoMedico.ConferenciaPagamentoRealizada,
                        RepasseMedico = p.FaturamentoMedico.RepasseMedico,
                        RepasseMedicoObservacao = p.FaturamentoMedico.RepasseMedicoObservacao,
                        TipoFaturamentoParticular = p.FaturamentoMedico.TipoFaturamentoParticular,
                        ReciboNotaContrato = p.FaturamentoMedico.ReciboNotaContrato,
                        Observacoes = p.FaturamentoMedico.Observacoes,
                        DataCadastro = p.FaturamentoMedico.DataCadastro,
                        DataAtualizacao = p.FaturamentoMedico.DataAtualizacao
                    },
                    ObservacoesNaoLidasCount = p.Observacoes.Count(observacao =>
                        observacao.DestinatarioUserId == request.CurrentUserId
                        && observacao.DataLeitura == null)
                })
                .ToListAsync(cancellationToken);

            foreach (var paciente in pacientes)
            {
                PacienteMapper.NormalizeProcedureCodes(paciente);
            }

            return new PagedResult<PacienteDto>
            {
                Items = pacientes,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar pacientes");
            throw;
        }
    }

    private static IQueryable<Paciente> ApplyFilters(
        IQueryable<Paciente> query,
        string? search,
        string digits,
        string? medico,
        string? convenio,
        string? procedimento)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.NomePaciente.Contains(search)
                || (p.Diagnostico != null && p.Diagnostico.Contains(search))
                || p.User.Email.Contains(search)
                || p.User.Telefone.Contains(search)
                || (p.HospitalReferencia != null && p.HospitalReferencia.Nome.Contains(search))
                || (p.Hospital != null && p.Hospital.Contains(search))
                || (p.MedicoUser != null && p.MedicoUser.Nome.Contains(search))
                || (p.Medico != null && p.Medico.Contains(search))
                || (p.MedicoAuxiliar1User != null && p.MedicoAuxiliar1User.Nome.Contains(search))
                || (p.MedicoAuxiliar1 != null && p.MedicoAuxiliar1.Contains(search))
                || (p.MedicoAuxiliar2User != null && p.MedicoAuxiliar2User.Nome.Contains(search))
                || (p.MedicoAuxiliar2 != null && p.MedicoAuxiliar2.Contains(search))
                || (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(search))
                || (p.Convenio != null && p.Convenio.Contains(search))
                || (p.OpmeFornecedorReferencia != null && p.OpmeFornecedorReferencia.Fornecedor.Contains(search))
                || (p.OpmeFornecedor != null && p.OpmeFornecedor.Contains(search))
                || (p.Procedimento != null && p.Procedimento.Contains(search))
                || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
                || (!string.IsNullOrEmpty(digits)
                    && p.CbhpmCodigo != null
                    && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                || p.Procedimentos.Any(item =>
                    item.Procedimento.Contains(search)
                    || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                    || (!string.IsNullOrEmpty(digits)
                        && item.CbhpmCodigo != null
                        && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                    || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search)))
                || (!string.IsNullOrEmpty(digits) && p.User.Cpf != null && p.User.Cpf.Contains(digits))
                || (!string.IsNullOrEmpty(digits) && p.User.Telefone.Contains(digits)));
        }

        if (!string.IsNullOrWhiteSpace(medico))
        {
            query = query.Where(p =>
                (p.MedicoUser != null && p.MedicoUser.Nome.Contains(medico))
                || (p.Medico != null && p.Medico.Contains(medico)));
        }

        if (!string.IsNullOrWhiteSpace(convenio))
        {
            query = query.Where(p =>
                (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(convenio))
                || (p.Convenio != null && p.Convenio.Contains(convenio)));
        }

        if (!string.IsNullOrWhiteSpace(procedimento))
        {
            query = query.Where(p =>
                (p.Procedimento != null && p.Procedimento.Contains(procedimento))
                || p.Procedimentos.Any(item => item.Procedimento.Contains(procedimento)));
        }

        return query;
    }
}
