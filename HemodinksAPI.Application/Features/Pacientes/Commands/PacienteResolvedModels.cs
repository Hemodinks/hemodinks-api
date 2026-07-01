using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

internal sealed record ResolvedHospital(int Id, string Nome, Hospital Referencia);

internal sealed record ResolvedConvenio(int Id, string Descricao, Convenio Referencia);

internal sealed record ResolvedOpmeFornecedor(int Id, string Fornecedor, HemodinksAPI.Domain.Models.Opme FornecedorReferencia);

internal sealed record ResolvedMedico(int? UserId, string? Nome);

internal sealed record ResolvedProcedimento(string? Codigo, string Nome, string? Porte, decimal? ValorReferencia);
