namespace HemodinksAPI.Application.Auditing;

public sealed record PlatformAuditEntry(
    int UsuarioGlobalId,
    int? ClinicaId,
    int? UserId,
    string Acao,
    string Recurso,
    string? EntidadeId,
    string? DetalhesJson,
    string? Ip,
    string? UserAgent,
    string? RequestId,
    bool Sucesso,
    DateTime DataCadastro);

public interface IPlatformAuditWriter
{
    Task WriteAsync(PlatformAuditEntry entry, CancellationToken cancellationToken);
}
