using HemodinksAPI.Application.Features.Pacientes.Queries;
using MediatR;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class CreatePacienteCommand : IRequest<PacienteDto>
{
    public DateTime? Data { get; set; }
    public string NomePaciente { get; set; } = null!;
    public string Cpf { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? FotoPerfil { get; set; }
    public DateTime DataNascimento { get; set; }
    public int? HospitalId { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public string? Medico { get; set; }
    public int? ConvenioId { get; set; }
    public string? Convenio { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public List<PacienteProcedimentoCommandDto> Procedimentos { get; set; } = [];
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public bool Ativo { get; set; } = true;
    public int CurrentUserId { get; set; }
    public int CurrentPerfilId { get; set; }
    public string CurrentUserName { get; set; } = string.Empty;
}

public class UpdatePacienteCommand : IRequest<PacienteDto>
{
    public int Id { get; set; }
    public DateTime? Data { get; set; }
    public string NomePaciente { get; set; } = null!;
    public string Cpf { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string? FotoPerfil { get; set; }
    public DateTime DataNascimento { get; set; }
    public int? HospitalId { get; set; }
    public string? Hospital { get; set; }
    public int? MedicoUserId { get; set; }
    public string? Medico { get; set; }
    public int? ConvenioId { get; set; }
    public string? Convenio { get; set; }
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public List<PacienteProcedimentoCommandDto> Procedimentos { get; set; } = [];
    public string? Autorizacao { get; set; }
    public string? Pagamento { get; set; }
    public string? RepasseGlosa { get; set; }
    public bool StatusPago { get; set; }
    public bool Ativo { get; set; }
    public int CurrentUserId { get; set; }
    public int CurrentPerfilId { get; set; }
    public string CurrentUserName { get; set; } = string.Empty;
}

public class DeletePacienteCommand : IRequest
{
    public int Id { get; set; }
    public int CurrentPerfilId { get; set; }
}

public class PacienteProcedimentoCommandDto
{
    public string? CbhpmCodigo { get; set; }
    public string? CbhpmPorte { get; set; }
    public string? Procedimento { get; set; }
    public decimal? ValorReferencia { get; set; }
}

public class UploadPacienteArquivoCommand : IRequest<PacienteArquivoDto>
{
    public int PacienteId { get; set; }
    public IFormFile File { get; set; } = null!;
    public int CurrentUserId { get; set; }
    public int CurrentPerfilId { get; set; }
}

public class DeletePacienteArquivoCommand : IRequest
{
    public int PacienteId { get; set; }
    public int ArquivoId { get; set; }
    public int CurrentUserId { get; set; }
    public int CurrentPerfilId { get; set; }
}
