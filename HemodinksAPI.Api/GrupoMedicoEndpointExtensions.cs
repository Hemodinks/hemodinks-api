namespace HemodinksAPI.Api;

public static partial class GrupoMedicoEndpointExtensions
{
    public static void MapGrupoMedicoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/grupos-medicos")
            .WithTags("GruposMedicos")
            .RequireAuthorization();

        group.MapGet("/", GetAll)
            .WithSummary("Listar grupos medicos")
            .RequireAuthorization("Administrador");

        group.MapGet("/medicos", GetScopedMedicalUsers)
            .WithSummary("Listar medicos disponiveis conforme o escopo do usuario");

        group.MapGet("/{id}", GetById)
            .WithSummary("Buscar grupo medico por ID")
            .RequireAuthorization("Administrador");

        group.MapPost("/", Create)
            .WithSummary("Criar grupo medico")
            .RequireAuthorization("GrupoMedicoCadastrar");

        group.MapPut("/{id}", Update)
            .WithSummary("Atualizar grupo medico")
            .RequireAuthorization("Administrador");

        group.MapDelete("/{id}", Delete)
            .WithSummary("Excluir grupo medico")
            .RequireAuthorization("Administrador");
    }
}
