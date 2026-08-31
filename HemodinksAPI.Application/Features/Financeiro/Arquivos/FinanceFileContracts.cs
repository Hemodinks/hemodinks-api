using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed record ReceiptUploadResponse(int RecebimentoId, string Nome, string ContentType, long Tamanho, string Url);
public sealed record FinanceAuditItemDto(long Id, string Acao, string Recurso, string? EntidadeId,
    string? DetalhesJson, int? UserId, string? Ip, bool Sucesso, DateTime DataCadastro);

