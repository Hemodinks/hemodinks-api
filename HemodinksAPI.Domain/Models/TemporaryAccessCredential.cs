namespace HemodinksAPI.Domain.Models;

// One recovery slot per global identity. Historical events belong to the audit trail.
public sealed class TemporaryAccessCredential
{
    public int UsuarioGlobalId { get; set; }
    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int ClinicaId { get; set; }
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int CreatedByUserId { get; set; }
}
