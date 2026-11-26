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
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar)
            .WithPacienteAudit("paciente.listar");

        group.MapGet("/pendencias/data-consolidacao", GetDataConsolidacaoPendencias)
            .WithName("GetDataConsolidacaoPendencias")
            .WithSummary("Contar pacientes com ano da data da consolidacao fora da faixa permitida")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar)
            .RequireAuthorization("PacientePendenciasVisualizar");

        group.MapGet("/{id}", GetPacienteById)
            .WithName("GetPacienteById")
            .WithSummary("Buscar paciente por ID")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar)
            .WithPacienteAudit("paciente.ler");

        group.MapPost("/", CreatePaciente)
            .WithName("CreatePaciente")
            .WithSummary("Criar paciente")
            .RequireAuthorization("PacienteCadastrar")
            .RequireAuthorization("EquipeOperacaoSensivel")
            .WithPacienteAudit("paciente.criar");

        group.MapPut("/{id}", UpdatePaciente)
            .WithName("UpdatePaciente")
            .WithSummary("Atualizar paciente")
            .RequireAuthorization("PacienteEditar")
            .RequireAuthorization("EquipeOperacaoSensivel")
            .WithPacienteAudit("paciente.editar");

        // #369: DELETE so inativa (prontuario e arquivos preservados); restrito a Administrador.
        group.MapDelete("/{id}", DeletePaciente)
            .WithName("DeletePaciente")
            .WithSummary("Inativar paciente")
            .RequireAuthorization("PacienteExcluir")
            .WithPacienteAudit("paciente.inativar");

        group.MapPost("/{id}/reativacao", ReativarPaciente)
            .WithName("ReativarPaciente")
            .WithSummary("Reativar paciente inativado")
            .RequireAuthorization("PacienteExcluir")
            .WithPacienteAudit("paciente.reativar");

        group.MapPost("/{id}/arquivos", UploadArquivo)
            .WithName("UploadPacienteArquivo")
            .WithSummary("Enviar arquivo do paciente")
            .DisableAntiforgery()
            .RequireAuthorization("PacienteArquivosGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel")
            .WithPacienteAudit("arquivo.enviar");

        group.MapGet("/{id}/arquivos/{arquivoId}/download", DownloadArquivo)
            .WithName("DownloadPacienteArquivo")
            .WithSummary("Baixar arquivo do paciente")
            .RequireAuthorization(LicencaPolicies.PacientesVisualizar)
            .WithPacienteAudit("arquivo.baixar");

        group.MapDelete("/{id}/arquivos/{arquivoId}", DeleteArquivo)
            .WithName("DeletePacienteArquivo")
            .WithSummary("Excluir arquivo do paciente")
            .RequireAuthorization("PacienteArquivosGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel")
            .WithPacienteAudit("arquivo.excluir");

        group.MapGet("/{id}/observacoes", GetObservacoes)
            .WithName("GetPacienteObservacoes")
            .WithSummary("Listar observacoes do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar")
            .WithPacienteAudit("observacao.listar");

        group.MapPost("/{id}/observacoes", CreateObservacao)
            .WithName("CreatePacienteObservacao")
            .WithSummary("Registrar observacao do paciente")
            .RequireAuthorization("PacienteObservacaoGerenciar")
            .RequireAuthorization("EquipeOperacaoSensivel")
            .WithPacienteAudit("observacao.criar");

        group.MapPost("/{id}/observacoes/marcar-lidas", MarkObservacoesAsRead)
            .WithName("MarkPacienteObservacoesAsRead")
            .WithSummary("Marcar observacoes do paciente como lidas")
            .RequireAuthorization("PacienteObservacaoGerenciar")
            .WithPacienteAudit("observacao.marcar-lidas");
    }
}
