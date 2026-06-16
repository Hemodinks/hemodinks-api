using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.GruposMedicos;
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
            var medico = canUseAdminFilters ? TrimOptional(request.Medico) : null;
            var convenio = canUseAdminFilters ? TrimOptional(request.Convenio) : null;
            var procedimento = canUseAdminFilters ? TrimOptional(request.Procedimento) : null;

            var query = _context.Pacientes.AsNoTracking();
            query = PacienteAccess.ApplyScope(_context, query, request.CurrentPerfilId, request.CurrentUserId);

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

            var totalItems = await query.CountAsync(cancellationToken);
            query = ApplyOrdering(query, request.SortBy, request.SortDirection);

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

    private static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IQueryable<Paciente> ApplyOrdering(IQueryable<Paciente> query, string? sortBy, string? sortDirection)
    {
        var normalizedSortBy = NormalizeSortBy(sortBy);
        var isDescending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return normalizedSortBy switch
        {
            "nome" => isDescending
                ? query.OrderByDescending(paciente => paciente.NomePaciente).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
            "hospital" => isDescending
                ? query.OrderByDescending(paciente => paciente.HospitalReferencia != null ? paciente.HospitalReferencia.Nome : paciente.Hospital)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.HospitalReferencia != null ? paciente.HospitalReferencia.Nome : paciente.Hospital)
                    .ThenBy(paciente => paciente.Id),
            "medico" => isDescending
                ? query.OrderByDescending(paciente => paciente.MedicoUser != null ? paciente.MedicoUser.Nome : paciente.Medico)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.MedicoUser != null ? paciente.MedicoUser.Nome : paciente.Medico)
                    .ThenBy(paciente => paciente.Id),
            "convenio" => isDescending
                ? query.OrderByDescending(paciente => paciente.ConvenioReferencia != null ? paciente.ConvenioReferencia.DescricaoConvenio : paciente.Convenio)
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.ConvenioReferencia != null ? paciente.ConvenioReferencia.DescricaoConvenio : paciente.Convenio)
                    .ThenBy(paciente => paciente.Id),
            "auxiliares" => isDescending
                ? query.OrderByDescending(paciente =>
                        (paciente.MedicoAuxiliar1User != null ? paciente.MedicoAuxiliar1User.Nome : paciente.MedicoAuxiliar1) + " / "
                        + (paciente.MedicoAuxiliar2User != null ? paciente.MedicoAuxiliar2User.Nome : paciente.MedicoAuxiliar2))
                    .ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente =>
                        (paciente.MedicoAuxiliar1User != null ? paciente.MedicoAuxiliar1User.Nome : paciente.MedicoAuxiliar1) + " / "
                        + (paciente.MedicoAuxiliar2User != null ? paciente.MedicoAuxiliar2User.Nome : paciente.MedicoAuxiliar2))
                    .ThenBy(paciente => paciente.Id),
            "status" => isDescending
                ? query.OrderByDescending(paciente => paciente.StatusPago).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.StatusPago).ThenBy(paciente => paciente.Id),
            "arquivos" => isDescending
                ? query.OrderByDescending(paciente => paciente.Arquivos.Count).ThenByDescending(paciente => paciente.NomePaciente).ThenByDescending(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.Arquivos.Count).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
            _ => isDescending
                ? query.OrderByDescending(paciente => paciente.User.DataAtualizacao ?? paciente.User.DataCadastro).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id)
                : query.OrderBy(paciente => paciente.User.DataAtualizacao ?? paciente.User.DataCadastro).ThenBy(paciente => paciente.NomePaciente).ThenBy(paciente => paciente.Id),
        };
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "recent"
            : sortBy.Trim().ToLowerInvariant();
    }
}

public class GetPacienteByIdQueryHandler : IRequestHandler<GetPacienteByIdQuery, PacienteDto?>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetPacienteByIdQueryHandler> _logger;

    public GetPacienteByIdQueryHandler(IAppDbContext context, ILogger<GetPacienteByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PacienteDto?> Handle(GetPacienteByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IQueryable<Paciente> query = _context.Pacientes
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.MedicoUser)
                .Include(p => p.MedicoAuxiliar1User)
                .Include(p => p.MedicoAuxiliar2User)
                .Include(p => p.HospitalReferencia)
                .Include(p => p.ConvenioReferencia)
                .Include(p => p.OpmeFornecedorReferencia)
                .Include(p => p.Procedimentos)
                .Include(p => p.Observacoes)
                .Include(p => p.Arquivos);

            query = PacienteAccess.ApplyScope(_context, query, request.CurrentPerfilId, request.CurrentUserId);

            var paciente = await query
                .Where(p => p.Id == request.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (paciente == null)
            {
                return null;
            }

            var dto = PacienteMapper.ToDto(paciente);
            dto.ObservacoesNaoLidasCount = paciente.Observacoes.Count(observacao =>
                observacao.DestinatarioUserId == request.CurrentUserId
                && observacao.DataLeitura == null);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}

internal static class PacienteAccess
{
    public static IQueryable<Paciente> ApplyScope(
        IAppDbContext context,
        IQueryable<Paciente> query,
        int perfilId,
        int userId)
    {
        if (perfilId == Perfil.AdministradorId || perfilId == Perfil.ControllerId)
        {
            return query;
        }

        if (perfilId == Perfil.MedicosId)
        {
            var accessibleMedicalUserIds = MedicalGroupScope.BuildScopedMedicalUserIdsQuery(context, perfilId, userId);
            return query.Where(p =>
                (p.MedicoUserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoUserId.Value))
                || (p.MedicoAuxiliar1UserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoAuxiliar1UserId.Value))
                || (p.MedicoAuxiliar2UserId.HasValue && accessibleMedicalUserIds.Contains(p.MedicoAuxiliar2UserId.Value)));
        }

        if (perfilId == Perfil.PacientesId)
        {
            return query.Where(p => p.UserId == userId);
        }

        return query.Where(_ => false);
    }

}

internal static class PacienteMapper
{
    public static PacienteDto ToDto(Paciente paciente)
    {
        var dto = new PacienteDto
        {
            Id = paciente.Id,
            UserId = paciente.UserId,
            Data = paciente.Data,
            DataCadastro = paciente.User.DataCadastro,
            DataAtualizacao = paciente.User.DataAtualizacao,
            NomePaciente = paciente.NomePaciente,
            Diagnostico = paciente.Diagnostico,
            TratamentoMedico = paciente.TratamentoMedico,
            HospitalId = paciente.HospitalId,
            Hospital = paciente.HospitalReferencia?.Nome ?? paciente.Hospital,
            MedicoUserId = paciente.MedicoUserId,
            Medico = paciente.MedicoUser?.Nome ?? paciente.Medico,
            MedicoAuxiliar1UserId = paciente.MedicoAuxiliar1UserId,
            MedicoAuxiliar1 = paciente.MedicoAuxiliar1User?.Nome ?? paciente.MedicoAuxiliar1,
            MedicoAuxiliar2UserId = paciente.MedicoAuxiliar2UserId,
            MedicoAuxiliar2 = paciente.MedicoAuxiliar2User?.Nome ?? paciente.MedicoAuxiliar2,
            ConvenioId = paciente.ConvenioId,
            Convenio = paciente.ConvenioReferencia?.DescricaoConvenio ?? paciente.Convenio,
            OpmeFornecedorId = paciente.OpmeFornecedorId,
            OpmeFornecedor = paciente.OpmeFornecedorReferencia?.Fornecedor ?? paciente.OpmeFornecedor,
            CbhpmCodigo = paciente.CbhpmCodigo,
            CbhpmPorte = paciente.CbhpmPorte,
            Procedimento = paciente.Procedimento,
            Procedimentos = ToProcedimentoDtos(paciente),
            Autorizacao = paciente.Autorizacao,
            Pagamento = paciente.Pagamento,
            RepasseGlosa = paciente.RepasseGlosa,
            StatusPago = paciente.StatusPago,
            Cpf = paciente.User.Cpf,
            Email = paciente.User.Email,
            Telefone = paciente.User.Telefone,
            FotoPerfil = paciente.User.FotoPerfil,
            DataNascimento = paciente.User.DataNascimento,
            Ativo = paciente.User.Ativo,
            ArquivosCount = paciente.Arquivos.Count,
            Arquivos = paciente.Arquivos
                .OrderByDescending(arquivo => arquivo.DataUpload)
                .Select(ToArquivoDto)
                .ToList()
        };

        return NormalizeProcedureCodes(dto);
    }

    public static PacienteDto NormalizeProcedureCodes(PacienteDto paciente)
    {
        paciente.CbhpmCodigo = CbhpmCodigoUtils.NormalizeOptional(paciente.CbhpmCodigo);
        foreach (var procedimento in paciente.Procedimentos)
        {
            procedimento.CbhpmCodigo = CbhpmCodigoUtils.NormalizeOptional(procedimento.CbhpmCodigo);
        }

        return paciente;
    }

    private static List<PacienteProcedimentoDto> ToProcedimentoDtos(Paciente paciente)
    {
        var procedimentos = paciente.Procedimentos
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
            .ToList();

        if (procedimentos.Count > 0 || string.IsNullOrWhiteSpace(paciente.Procedimento))
        {
            return procedimentos;
        }

        return
        [
            new PacienteProcedimentoDto
            {
                CbhpmCodigo = paciente.CbhpmCodigo,
                CbhpmPorte = paciente.CbhpmPorte,
                Procedimento = paciente.Procedimento,
                Ordem = 1
            }
        ];
    }

    public static PacienteArquivoDto ToArquivoDto(PacienteArquivo arquivo)
    {
        return new PacienteArquivoDto
        {
            Id = arquivo.Id,
            NomeOriginal = arquivo.NomeOriginal,
            ContentType = arquivo.ContentType,
            TamanhoBytes = arquivo.TamanhoBytes,
            Url = arquivo.Url,
            DataUpload = arquivo.DataUpload
        };
    }
}
