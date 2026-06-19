using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Faturamentos.Queries;

public class GetAllFaturamentosMedicosQueryHandler : IRequestHandler<GetAllFaturamentosMedicosQuery, PagedResult<PacienteDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllFaturamentosMedicosQueryHandler> _logger;

    public GetAllFaturamentosMedicosQueryHandler(IAppDbContext context, ILogger<GetAllFaturamentosMedicosQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<PacienteDto>> Handle(GetAllFaturamentosMedicosQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());
            var canUseGlobalFilters = request.CurrentPerfilId is Perfil.AdministradorId or Perfil.ControllerId;
            var medico = canUseGlobalFilters ? TrimOptional(request.Medico) : null;
            var convenio = TrimOptional(request.Convenio);
            var procedimento = TrimOptional(request.Procedimento);

            var query = ApplyFaturamentoScope(
                _context.Pacientes.AsNoTracking(),
                request.CurrentPerfilId,
                request.CurrentUserId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.NomePaciente.Contains(search)
                    || (p.User.Email.Contains(search))
                    || (p.HospitalReferencia != null && p.HospitalReferencia.Nome.Contains(search))
                    || (p.Hospital != null && p.Hospital.Contains(search))
                    || (p.MedicoUser != null && p.MedicoUser.Nome.Contains(search))
                    || (p.Medico != null && p.Medico.Contains(search))
                    || (p.ConvenioReferencia != null && p.ConvenioReferencia.DescricaoConvenio.Contains(search))
                    || (p.Convenio != null && p.Convenio.Contains(search))
                    || (p.OpmeFornecedorReferencia != null && p.OpmeFornecedorReferencia.Fornecedor.Contains(search))
                    || (p.OpmeFornecedor != null && p.OpmeFornecedor.Contains(search))
                    || (p.Procedimento != null && p.Procedimento.Contains(search))
                    || (p.CbhpmCodigo != null && p.CbhpmCodigo.Contains(search))
                    || (p.Autorizacao != null && p.Autorizacao.Contains(search))
                    || (p.FaturamentoMedico != null
                        && ((p.FaturamentoMedico.GuiaAutorizacaoConvenio != null && p.FaturamentoMedico.GuiaAutorizacaoConvenio.Contains(search))
                            || (p.FaturamentoMedico.CodigoTussCbhpmAmb != null && p.FaturamentoMedico.CodigoTussCbhpmAmb.Contains(search))
                            || (p.FaturamentoMedico.GlosaStatus != null && p.FaturamentoMedico.GlosaStatus.Contains(search))))
                    || (!string.IsNullOrEmpty(digits)
                        && p.CbhpmCodigo != null
                        && p.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                    || p.Procedimentos.Any(item =>
                        item.Procedimento.Contains(search)
                        || (item.CbhpmCodigo != null && item.CbhpmCodigo.Contains(search))
                        || (!string.IsNullOrEmpty(digits)
                            && item.CbhpmCodigo != null
                            && item.CbhpmCodigo.Replace(".", "").Replace("-", "").Contains(digits))
                        || (item.CbhpmPorte != null && item.CbhpmPorte.Contains(search))));
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

            var totalItems = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(p => p.Data ?? p.User.DataAtualizacao ?? p.User.DataCadastro)
                .ThenBy(p => p.NomePaciente)
                .ThenBy(p => p.Id)
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
                    TratamentoMedico = p.TratamentoMedico,
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
                    Faturamento = PacienteMapper.ToFaturamentoDto(p.FaturamentoMedico)
                })
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                PacienteMapper.NormalizeProcedureCodes(item);
            }

            return new PagedResult<PacienteDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar faturamentos medicos");
            throw;
        }
    }

    private static IQueryable<Paciente> ApplyFaturamentoScope(IQueryable<Paciente> query, int perfilId, int currentUserId)
    {
        if (perfilId == Perfil.AdministradorId || perfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            return query.Where(p => p.MedicoUserId == currentUserId);
        }

        return query.Where(_ => false);
    }

    private static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
