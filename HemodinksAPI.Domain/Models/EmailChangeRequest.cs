namespace HemodinksAPI.Domain.Models;

// This proof authorizes only confirmation of the requested authentication email.
public sealed class EmailChangeRequest
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int UsuarioGlobalId { get; set; }
    public int UsuarioClinicaId { get; set; }
    public int ClinicaId { get; set; }
    public Guid SecurityVersion { get; set; }
    public Guid ContextVersion { get; set; }
    public string NewEmail { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
