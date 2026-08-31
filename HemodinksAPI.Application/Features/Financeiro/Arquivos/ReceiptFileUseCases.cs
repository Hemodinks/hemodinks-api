using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed partial class FinanceiroFileUseCases
{
    public async Task<ReceiptUploadResponse> UploadReceiptAsync(
        int receiptId,
        UploadedFile file,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".jpg" and not ".jpeg")
            throw new InvalidOperationException("O comprovante deve estar no formato PDF ou JPG.");

        var receipt = await db.Recebimentos.SingleOrDefaultAsync(item => item.Id == receiptId, cancellationToken)
            ?? throw new KeyNotFoundException("Recebimento nao encontrado.");
        var oldUrl = receipt.DocumentoComprovante;
        var stored = await storage.SaveAsync(file, cancellationToken);
        receipt.DocumentoComprovante = stored.Url;
        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(oldUrl)) await storage.DeleteAsync(oldUrl, cancellationToken);
        return new ReceiptUploadResponse(receiptId, stored.OriginalName, stored.ContentType, stored.SizeBytes, stored.Url);
    }

    public async Task<PrivateFileDownload> DownloadReceiptAsync(int receiptId, CancellationToken cancellationToken)
    {
        var receipt = await db.Recebimentos.AsNoTracking().SingleOrDefaultAsync(item => item.Id == receiptId, cancellationToken)
            ?? throw new KeyNotFoundException("Recebimento nao encontrado.");
        var storedUrl = receipt.DocumentoComprovante ?? throw new KeyNotFoundException("Comprovante nao encontrado.");
        var file = await storage.GetAsync(storedUrl, cancellationToken)
            ?? throw new KeyNotFoundException("Comprovante nao encontrado.");
        var storedPath = Uri.TryCreate(storedUrl, UriKind.Absolute, out var storedUri) ? storedUri.AbsolutePath : storedUrl;
        var extension = Path.GetExtension(storedPath).ToLowerInvariant();
        var (contentType, downloadExtension) = extension switch
        {
            ".pdf" => ("application/pdf", ".pdf"),
            ".jpg" or ".jpeg" => ("image/jpeg", ".jpg"),
            _ => throw new InvalidOperationException("O formato do comprovante armazenado não é suportado.")
        };
        return new PrivateFileDownload
        {
            Content = file.Content,
            ContentType = contentType,
            FileName = $"comprovante-{receiptId}{downloadExtension}"
        };
    }

}
