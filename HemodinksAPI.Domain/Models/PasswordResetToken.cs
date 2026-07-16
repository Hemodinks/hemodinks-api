namespace HemodinksAPI.Domain.Models;

public class PasswordResetToken : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; } = Clinica.DefaultId;

    public Clinica Clinica { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UsedAt { get; set; }

    public string? RequestIp { get; set; }
}
