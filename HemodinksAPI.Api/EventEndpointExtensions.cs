using System.Security.Claims;
using HemodinksAPI.Api.Authorization;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static class EventEndpointExtensions
{
    private const int DefaultReminderPeriodMinutes = 24 * 60;
    private const int MinimumReminderPeriodMinutes = 15;
    private const int MaximumReminderPeriodMinutes = 7 * 24 * 60;

    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Agenda")
            .RequireAuthorization();

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Listar eventos da agenda");

        group.MapGet("/medical-users", GetMedicalUsers)
            .WithName("GetEventMedicalUsers")
            .WithSummary("Listar medicos ativos para notificacao de eventos");

        group.MapGet("/{id:int}", GetEventById)
            .WithName("GetEventById")
            .WithSummary("Buscar evento da agenda por ID");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Criar evento na agenda");

        group.MapPut("/{id:int}", UpdateEvent)
            .WithName("UpdateEvent")
            .WithSummary("Atualizar evento da agenda");

        group.MapPost("/{id:int}/complete", CompleteEvent)
            .WithName("CompleteEvent")
            .WithSummary("Marcar evento como concluido");

        group.MapDelete("/{id:int}", DeleteEvent)
            .WithName("DeleteEvent")
            .WithSummary("Excluir evento da agenda");
    }

    private static async Task<IResult> GetMedicalUsers(
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = await db.Users
                .AsNoTracking()
                .Where(user => user.Ativo && user.PerfilId == Perfil.MedicosId)
                .OrderBy(user => user.Nome)
                .Select(user => new EventMedicalUserDto
                {
                    Id = user.Id,
                    Nome = user.Nome
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(users);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar medicos para agenda");
            return Results.BadRequest(new { message = "Erro ao buscar medicos para agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> GetEvents(
        DateTime? from,
        DateTime? to,
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var query = ApplyEventScope(db.Events.AsNoTracking(), currentUser);

            if (from.HasValue)
            {
                var fromUtc = ToUtc(from.Value);
                query = query.Where(ev => ev.End >= fromUtc);
            }

            if (to.HasValue)
            {
                var toUtc = ToUtc(to.Value);
                query = query.Where(ev => ev.Start <= toUtc);
            }

            var events = await query
                .Include(ev => ev.User)
                .Include(ev => ev.MedicalUser)
                .OrderBy(ev => ev.Start)
                .ThenBy(ev => ev.Title)
                .ToListAsync(cancellationToken);

            return Results.Ok(events.Select(ToDto).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar eventos da agenda");
            return Results.BadRequest(new { message = "Erro ao buscar eventos da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> GetEventById(
        int id,
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var ev = await ApplyEventScope(db.Events.AsNoTracking(), currentUser)
                .Include(item => item.User)
                .Include(item => item.MedicalUser)
                .Where(item => item.Id == id)
                .FirstOrDefaultAsync(cancellationToken);

            return ev == null ? Results.NotFound() : Results.Ok(ToDto(ev));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao buscar evento da agenda {EventId}", id);
            return Results.BadRequest(new { message = "Erro ao buscar evento da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> CreateEvent(
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        EventRequest request,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var ownerUserId = await ResolveOwnerUserIdAsync(db, request.UserId, currentUser, cancellationToken);
            var medicalUserId = await ResolveMedicalUserIdAsync(db, request, currentUser, cancellationToken);
            var ev = BuildEvent(new Event(), request, ownerUserId, medicalUserId, isCreate: true);

            db.Events.Add(ev);
            await db.SaveChangesAsync(cancellationToken);

            var created = await db.Events
                .AsNoTracking()
                .Include(item => item.User)
                .Include(item => item.MedicalUser)
                .Where(item => item.Id == ev.Id)
                .FirstAsync(cancellationToken);

            return Results.Created($"/api/events/{ev.Id}", ToDto(created));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao criar evento da agenda");
            return Results.BadRequest(new { message = "Erro ao criar evento da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        EventRequest request,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var ev = await db.Events.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (ev == null)
            {
                return Results.NotFound();
            }

            EnsureCanManageEvent(ev, currentUser);

            var ownerUserId = await ResolveOwnerUserIdAsync(db, request.UserId ?? ev.UserId, currentUser, cancellationToken);
            var medicalUserId = await ResolveMedicalUserIdAsync(db, request, currentUser, cancellationToken);
            BuildEvent(ev, request, ownerUserId, medicalUserId, isCreate: false);

            await db.SaveChangesAsync(cancellationToken);

            var updated = await db.Events
                .AsNoTracking()
                .Include(item => item.User)
                .Include(item => item.MedicalUser)
                .Where(item => item.Id == id)
                .FirstAsync(cancellationToken);

            return Results.Ok(ToDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar evento da agenda {EventId}", id);
            return Results.BadRequest(new { message = "Erro ao atualizar evento da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> CompleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var ev = await db.Events.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (ev == null)
            {
                return Results.NotFound();
            }

            EnsureCanManageEvent(ev, currentUser);

            ev.IsCompleted = true;
            ev.CompletedAt = DateTime.UtcNow;
            ev.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao concluir evento da agenda {EventId}", id);
            return Results.BadRequest(new { message = "Erro ao concluir evento da agenda", error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteEvent(
        int id,
        ClaimsPrincipal claimsPrincipal,
        AppDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = claimsPrincipal.ToCurrentUserContext();
            if (currentUser == null)
            {
                return Results.Forbid();
            }

            var ev = await db.Events.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (ev == null)
            {
                return Results.NotFound();
            }

            EnsureCanManageEvent(ev, currentUser);

            db.Events.Remove(ev);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao excluir evento da agenda {EventId}", id);
            return Results.BadRequest(new { message = "Erro ao excluir evento da agenda", error = ex.Message });
        }
    }

    private static IQueryable<Event> ApplyEventScope(IQueryable<Event> query, CurrentUserContext currentUser)
    {
        if (currentUser.IsAdministrador)
        {
            return query;
        }

        if (currentUser.IsMedico)
        {
            return query.Where(ev =>
                ev.UserId == currentUser.Id
                || ev.MedicalUserId == currentUser.Id
                || (ev.NotifyMedicalProfile && ev.MedicalUserId == null));
        }

        return query.Where(ev => ev.UserId == currentUser.Id);
    }

    private static void EnsureCanManageEvent(Event ev, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAdministrador && ev.UserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
        }
    }

    private static async Task<int> ResolveOwnerUserIdAsync(
        AppDbContext db,
        int? requestedUserId,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var ownerUserId = requestedUserId ?? currentUser.Id;

        if (!currentUser.IsAdministrador && ownerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException();
        }

        var ownerExists = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == ownerUserId && user.Ativo, cancellationToken);

        if (!ownerExists)
        {
            throw new InvalidOperationException("Usuario responsavel pelo evento nao encontrado ou inativo.");
        }

        return ownerUserId;
    }

    private static async Task<int?> ResolveMedicalUserIdAsync(
        AppDbContext db,
        EventRequest request,
        CurrentUserContext currentUser,
        CancellationToken cancellationToken)
    {
        var medicalUserId = request.MedicalUserId;

        if (request.NotifyMedicalProfile && !medicalUserId.HasValue && currentUser.IsMedico)
        {
            medicalUserId = currentUser.Id;
        }

        if (!medicalUserId.HasValue)
        {
            return null;
        }

        var isValidMedicalUser = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == medicalUserId.Value
                && user.Ativo
                && user.PerfilId == Perfil.MedicosId, cancellationToken);

        if (!isValidMedicalUser)
        {
            throw new InvalidOperationException("Medico selecionado para notificacao nao encontrado ou inativo.");
        }

        return medicalUserId.Value;
    }

    private static Event BuildEvent(
        Event ev,
        EventRequest request,
        int userId,
        int? medicalUserId,
        bool isCreate)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Informe o titulo do evento.");
        }

        var start = ToUtc(request.Start);
        var end = ToUtc(request.End);
        if (end <= start)
        {
            throw new InvalidOperationException("A data final do evento deve ser maior que a data inicial.");
        }

        var reminderPeriodMinutes = request.ReminderPeriodMinutes;
        if (request.NotifyUser || request.NotifyMedicalProfile)
        {
            reminderPeriodMinutes ??= DefaultReminderPeriodMinutes;
        }

        if (reminderPeriodMinutes.HasValue
            && (reminderPeriodMinutes.Value < MinimumReminderPeriodMinutes
                || reminderPeriodMinutes.Value > MaximumReminderPeriodMinutes))
        {
            throw new InvalidOperationException("O periodo de lembrete deve ficar entre 15 minutos e 7 dias.");
        }

        ev.UserId = userId;
        ev.MedicalUserId = medicalUserId;
        ev.Title = title;
        ev.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ev.Start = start;
        ev.End = end;
        ev.NotifyMedicalProfile = request.NotifyMedicalProfile;
        ev.NotifyUser = request.NotifyUser;
        ev.ReminderPeriodMinutes = reminderPeriodMinutes;
        ev.UpdatedAt = isCreate ? null : DateTime.UtcNow;

        if (request.IsCompleted.HasValue)
        {
            ev.IsCompleted = request.IsCompleted.Value;
            ev.CompletedAt = ev.IsCompleted
                ? ev.CompletedAt ?? DateTime.UtcNow
                : null;
        }

        return ev;
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static EventDto ToDto(Event ev)
    {
        return new EventDto
        {
            Id = ev.Id,
            UserId = ev.UserId,
            UserName = ev.User.Nome,
            MedicalUserId = ev.MedicalUserId,
            MedicalUserName = ev.MedicalUser != null ? ev.MedicalUser.Nome : null,
            Title = ev.Title,
            Description = ev.Description,
            Start = ev.Start,
            End = ev.End,
            NotifyMedicalProfile = ev.NotifyMedicalProfile,
            NotifyUser = ev.NotifyUser,
            ReminderPeriodMinutes = ev.ReminderPeriodMinutes,
            LastReminderSentAt = ev.LastReminderSentAt,
            IsCompleted = ev.IsCompleted,
            CompletedAt = ev.CompletedAt,
            CreatedAt = ev.CreatedAt,
            UpdatedAt = ev.UpdatedAt
        };
    }

    public sealed class EventRequest
    {
        public int? UserId { get; set; }

        public int? MedicalUserId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public bool NotifyMedicalProfile { get; set; }

        public bool NotifyUser { get; set; }

        public int? ReminderPeriodMinutes { get; set; }

        public bool? IsCompleted { get; set; }
    }

    public sealed class EventDto
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public int? MedicalUserId { get; set; }

        public string? MedicalUserName { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public bool NotifyMedicalProfile { get; set; }

        public bool NotifyUser { get; set; }

        public int? ReminderPeriodMinutes { get; set; }

        public DateTime? LastReminderSentAt { get; set; }

        public bool IsCompleted { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class EventMedicalUserDto
    {
        public int Id { get; set; }

        public string Nome { get; set; } = string.Empty;
    }
}
