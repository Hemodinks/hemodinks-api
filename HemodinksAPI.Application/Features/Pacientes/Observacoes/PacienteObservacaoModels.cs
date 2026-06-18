using MediatR;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class PacienteObservacaoDto
{
    public int Id { get; set; }

    public int PacienteId { get; set; }

    public int? ObservacaoPaiId { get; set; }

    public string Texto { get; set; } = string.Empty;

    public DateTime DataCadastro { get; set; }

    public DateTime? DataLeitura { get; set; }

    public int AutorUserId { get; set; }

    public string AutorNome { get; set; } = string.Empty;

    public int AutorPerfilId { get; set; }

    public string AutorPerfilNome { get; set; } = string.Empty;

    public int DestinatarioUserId { get; set; }

    public string DestinatarioNome { get; set; } = string.Empty;

    public int DestinatarioPerfilId { get; set; }

    public string DestinatarioPerfilNome { get; set; } = string.Empty;

    public string NomePaciente { get; set; } = string.Empty;

    public int? MedicoUserId { get; set; }

    public string? Medico { get; set; }

    public int? MedicoAuxiliar1UserId { get; set; }

    public string? MedicoAuxiliar1 { get; set; }

    public int? MedicoAuxiliar2UserId { get; set; }

    public string? MedicoAuxiliar2 { get; set; }

    public bool FoiLida { get; set; }

    public bool EnviadaPorMim { get; set; }
}

public class CreatePacienteObservacaoCommand : IRequest<CreatePacienteObservacaoResult>
{
    public int PacienteId { get; set; }

    public int? ObservacaoPaiId { get; set; }

    public string Texto { get; set; } = string.Empty;

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }

    public string CurrentUserName { get; set; } = string.Empty;
}

public class CreatePacienteObservacaoResult
{
    public int PacienteId { get; set; }

    public int CreatedCount { get; set; }
}

public class GetPacienteObservacoesQuery : IRequest<IReadOnlyList<PacienteObservacaoDto>>
{
    public int PacienteId { get; set; }

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }
}

public class MarkPacienteObservacoesAsReadCommand : IRequest<MarkPacienteObservacoesAsReadResult>
{
    public int PacienteId { get; set; }

    public int CurrentUserId { get; set; }

    public int CurrentPerfilId { get; set; }
}

public class MarkPacienteObservacoesAsReadResult
{
    public int PacienteId { get; set; }

    public int UpdatedCount { get; set; }
}
