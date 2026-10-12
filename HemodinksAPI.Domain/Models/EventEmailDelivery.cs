namespace HemodinksAPI.Domain.Models;

public sealed class EventEmailDelivery : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int RecipientUserId { get; set; }
    public User RecipientUser { get; set; } = null!;
    public string RecipientEmail { get; set; } = null!;
    public int SenderUserId { get; set; }
    public User SenderUser { get; set; } = null!;
    public int? SenderEquipeId { get; set; }
    public int? SenderEquipeOperadorId { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntil { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? SentAt { get; set; }
}

