namespace HemodinksAPI.Application.Features.Events;

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

    public string? NotificationMessage { get; set; }

    public bool NotifyAllAllowedRecipients { get; set; }

    public List<int> NotificationUserIds { get; set; } = [];

    public List<int> NotificationGroupIds { get; set; } = [];
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

    public DateTime? NextReminderAt { get; set; }

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

public sealed class AgendaNotificationRecipientUserDto
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public int PerfilId { get; set; }

    public string PerfilNome { get; set; } = string.Empty;
}

public sealed class AgendaNotificationRecipientGroupDto
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public int MembrosCount { get; set; }
}

public sealed class AgendaNotificationRecipientOptionsDto
{
    public bool CanNotifyAllAllowedRecipients { get; set; }

    public string AllRecipientsLabel { get; set; } = string.Empty;

    public List<AgendaNotificationRecipientUserDto> Users { get; set; } = [];

    public List<AgendaNotificationRecipientGroupDto> Groups { get; set; } = [];
}
