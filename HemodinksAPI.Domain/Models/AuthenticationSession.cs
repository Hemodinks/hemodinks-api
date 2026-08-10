namespace HemodinksAPI.Domain.Models;

public class AuthenticationSession
{
    public Guid Id { get; set; }

    public int UsuarioGlobalId { get; set; }

    public UsuarioGlobal UsuarioGlobal { get; set; } = null!;

    public int UsuarioClinicaId { get; set; }

    public UsuarioClinica UsuarioClinica { get; set; } = null!;

    public string RefreshTokenHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
