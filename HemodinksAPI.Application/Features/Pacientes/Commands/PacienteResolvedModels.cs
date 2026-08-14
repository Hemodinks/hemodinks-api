namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal sealed record ResolvedMedico(int? UserId, string? Nome);

internal sealed record ResolvedProcedimento(string? Codigo, string Nome, string? Porte, decimal? ValorReferencia);
