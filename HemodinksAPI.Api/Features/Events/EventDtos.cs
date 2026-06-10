namespace HemodinksAPI.Api.Features.Events;

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
