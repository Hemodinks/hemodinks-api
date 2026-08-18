using HemodinksAPI.Application.Features.Licencas;

namespace HemodinksAPI.Api;

public static partial class PacienteEndpointExtensions
{
    public static void MapPacienteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/pacientes")
            .WithTags("Pacientes")
            .RequireAuthorization();

        group.MapGet("/", GetAllPacientes)
            .WithName("GetAllPacientes")
            .WithSummary("Listar pacientes")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar);

        group.MapGet("/{id}", GetPacienteById)
            .WithName("GetPacienteById")
            .WithSummary("Buscar paciente por ID")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar);

        group.MapPost("/", CreatePaciente)
            .WithName("CreatePaciente")
            .WithSummary("Criar paciente")
            .RequireAuthorization("PacienteCadastrar")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapPut("/{id}", UpdatePaciente)
            .WithName("UpdatePaciente")
            .WithSummary("Atualizar paciente")
            .RequireAuthorization("PacienteEditar")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapDelete("/{id}", DeletePaciente)
            .WithName("DeletePaciente")
            .WithSummary("Excluir paciente")
            .RequireAuthorization("PacienteExcluir");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadPacienteArquivo")
            .WithSummary("Enviar arquivo do paciente")
            .DisableAntiforgery()
            .RequireAuthorization("PacienteArquivosGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapGet("/{id}/arquivos/{arquivoId}/download", DownloadArquivo)
            .WithName("DownloadPacienteArquivo")
            .WithSummary("Baixar arquivo do paciente")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar);

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeletePacienteArquivo")
            .WithSummary("Excluir arquivo do paciente")
            .RequireAuthorization("PacienteArquivosGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapGet("/{id}/observacoes", GetObservacoes)
            .WithName("GetPacienteObservacoes")
            .WithSummary("Listar observacoes do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar");

        group.MapPost("/{id}/observacoes", CreateObservacao)
            .WithName("CreatePacienteObservacao")
            .WithSummary("Registrar observacao do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapPost("/{id}/observacoes/marcar-lidas", MarkObservacoesAsRead)
            .WithName("MarkPacienteObservacoesAsRead")
            .WithSummary("Marcar observacoes do paciente como lidas")
            .RequireAuthorization("PacienteObservacaoGerenciar");
    }
}
