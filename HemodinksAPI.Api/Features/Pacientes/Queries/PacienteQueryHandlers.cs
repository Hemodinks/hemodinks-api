using HemodinksAPI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Pacientes.Queries;

public class GetAllPacientesQueryHandler : IRequestHandler<GetAllPacientesQuery, List<PacienteDto>>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetAllPacientesQueryHandler> _logger;

    public GetAllPacientesQueryHandler(AppDbContext context, ILogger<GetAllPacientesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<PacienteDto>> Handle(GetAllPacientesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var pacientes = await _context.Pacientes
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .OrderByDescending(p => p.Data ?? p.User.DataCadastro)
                .ToListAsync(cancellationToken);

            return pacientes.Select(PacienteMapper.ToDto).ToList();
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
