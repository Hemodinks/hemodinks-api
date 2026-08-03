namespace HemodinksAPI.Domain.Models;

public class IdempotencyRequest : IClinicaOwnedEntity
{
    public int Id { get; set; }

    public int ClinicaId { get; set; }

    public Clinica Clinica { get; set; } = null!;

    public string Operation { get; set; } = null!;

    public string Scope { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public string State { get; set; } = IdempotencyRequestStates.InProgress;

    public int? StatusCode { get; set; }

    public string? ResponseJson { get; set; }

    public string? ResourceLocation { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}

public static class IdempotencyRequestStates
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
}
