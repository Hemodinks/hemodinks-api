namespace HemodinksAPI.Domain.Models;

public class AgendaNotification : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public int EventId { get; set; }

    public Event Event { get; set; } = null!;

    public int SenderUserId { get; set; }

    public User SenderUser { get; set; } = null!;

    public int RecipientUserId { get; set; }

    public User RecipientUser { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }
}
