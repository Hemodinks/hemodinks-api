using MediatR;

namespace HemodinksAPI.Api.Features.Pacientes.Queries;

public class PacienteDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime? Data { get; set; }
    public string NomePaciente { get; set; } = null!;
    public string? Hospital { get; set; }
    public string? Medico { get; set; }
    public string? Convenio { get; set; }
    public string? Procedimento { get; set; }
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public string? Cpf { get; set; }
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? FotoPerfil { get; set; }
    public DateTime DataNascimento { get; set; }
    public bool Ativo { get; set; }
    public List<PacienteArquivoDto> Arquivos { get; set; } = [];
}

public class PacienteArquivoDto
{
    public int Id { get; set; }
    public string NomeOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string Url { get; set; } = null!;
    public DateTime DataUpload { get; set; }
}

public class GetAllPacientesQuery : IRequest<List<PacienteDto>>
{
}

public class GetPacienteByIdQuery : IRequest<PacienteDto?>
{
    public int Id { get; set; }

    public GetPacienteByIdQuery(int id)
    {
        Id = id;
    }
}
