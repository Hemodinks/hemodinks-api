namespace HemodinksAPI.Api.Models;

public class Event
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int? MedicalUserId { get; set; }

    public User? MedicalUser { get; set; }

    public string Title { get; set; } = null!;

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
