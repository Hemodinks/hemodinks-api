namespace HemodinksAPI.Api;

public static partial class EventEndpointExtensions
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Agenda e notificacoes")
            .RequireAuthorization();

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Listar eventos da agenda");

        group.MapGet("/medical-users", GetMedicalUsers)
            .WithName("GetEventMedicalUsers")
            .WithSummary("Listar medicos ativos para notificacao de eventos");

        group.MapGet("/notification-recipients", GetNotificationRecipients)
            .WithName("GetEventNotificationRecipients")
            .WithSummary("Listar destinatarios permitidos para notificacoes da agenda");

        group.MapPost("/notifications/mark-read", MarkNotificationsRead)
            .WithName("MarkAgendaNotificationsRead")
            .WithSummary("Marcar notificacoes da agenda como lidas");

        group.MapGet("/{id:int}", GetEventById)
            .WithName("GetEventById")
            .WithSummary("Buscar evento da agenda por ID");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Criar evento na agenda")
            .WithDescription("Cria evento na agenda. Envie Idempotency-Key para tornar retries seguros.")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapPut("/{id:int}", UpdateEvent)
            .WithName("UpdateEvent")
            .WithSummary("Atualizar evento da agenda")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapPost("/{id:int}/complete", CompleteEvent)
            .WithName("CompleteEvent")
            .WithSummary("Marcar evento como concluido")
            .RequireAuthorization("EquipeOperacaoSensivel");

        group.MapDelete("/{id:int}", DeleteEvent)
            .WithName("DeleteEvent")
            .WithSummary("Excluir evento da agenda")
            .RequireAuthorization("EquipeOperacaoSensivel");
    }
}
