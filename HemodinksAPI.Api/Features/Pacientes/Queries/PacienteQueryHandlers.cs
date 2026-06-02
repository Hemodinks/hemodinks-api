using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Pacientes.Queries;

public class GetAllPacientesQueryHandler : IRequestHandler<GetAllPacientesQuery, PagedResult<PacienteDto>>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetAllPacientesQueryHandler> _logger;

    public GetAllPacientesQueryHandler(AppDbContext context, ILogger<GetAllPacientesQueryHandler> logger)
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

            var query = _context.Pacientes.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.NomePaciente.Contains(search)
                    || p.User.Email.Contains(search)
                    || p.User.Telefone.Contains(search)
                    || (p.Hospital != null && p.Hospital.Contains(search))
                    || (p.Medico != null && p.Medico.Contains(search))
                    || (p.Convenio != null && p.Convenio.Contains(search))
                    || (p.Procedimento != null && p.Procedimento.Contains(search))
                    || (!string.IsNullOrEmpty(digits) && p.User.Cpf != null && p.User.Cpf.Contains(digits))
                    || (!string.IsNullOrEmpty(digits) && p.User.Telefone.Contains(digits)));
            }

            var totalItems = await query.CountAsync(cancellationToken);

            var pacientes = await query
                .OrderByDescending(p => p.Data ?? p.User.DataCadastro)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PacienteDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    Data = p.Data,
                    NomePaciente = p.NomePaciente,
                    Hospital = p.Hospital,
                    Medico = p.Medico,
                    Convenio = p.Convenio,
                    Procedimento = p.Procedimento,
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
                    ArquivosCount = p.Arquivos.Count
                })
                .ToListAsync(cancellationToken);

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
}

public class GetPacienteByIdQueryHandler : IRequestHandler<GetPacienteByIdQuery, PacienteDto?>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetPacienteByIdQueryHandler> _logger;

    public GetPacienteByIdQueryHandler(AppDbContext context, ILogger<GetPacienteByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PacienteDto?> Handle(GetPacienteByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var paciente = await _context.Pacientes
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .AsSplitQuery()
                .Where(p => p.Id == request.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return paciente == null ? null : PacienteMapper.ToDto(paciente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}

internal static class PacienteMapper
{
    public static PacienteDto ToDto(Models.Paciente paciente)
    {
        return new PacienteDto
        {
            Id = paciente.Id,
            UserId = paciente.UserId,
            Data = paciente.Data,
            NomePaciente = paciente.NomePaciente,
            Hospital = paciente.Hospital,
            Medico = paciente.Medico,
            Convenio = paciente.Convenio,
            Procedimento = paciente.Procedimento,
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
    }

    public static PacienteArquivoDto ToArquivoDto(Models.PacienteArquivo arquivo)
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
