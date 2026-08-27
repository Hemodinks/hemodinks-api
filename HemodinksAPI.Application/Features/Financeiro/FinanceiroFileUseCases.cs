using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class FinanceiroFileUseCases(
    IFinanceEndpointDbContext db,
    IPatientFileStorage storage,
    IClinicaContext tenant,
    ISender sender)
{
    public async Task<List<FaturamentoHistoricoArquivoDto>> ListHistoryFilesAsync(
        int? year,
        int? month,
        CancellationToken cancellationToken)
    {
        ValidateHistoryPeriod(year, month, requireBoth: false);
        var clinicId = tenant.GetRequiredClinicaId();
        var query = db.FaturamentoHistoricoArquivos.AsNoTracking()
            .Where(item => item.ClinicaId == clinicId);
        if (year.HasValue) query = query.Where(item => item.Ano == year.Value);
        if (month.HasValue) query = query.Where(item => item.Mes == month.Value);

        return await query
            .OrderByDescending(item => item.Ano)
            .ThenByDescending(item => item.Mes)
            .ThenByDescending(item => item.DataUpload)
            .Select(item => new FaturamentoHistoricoArquivoDto(
                item.Id,
                item.Ano,
                item.Mes,
                item.NomeOriginal,
                item.ContentType,
                item.TamanhoBytes,
                item.DataUpload))
            .ToListAsync(cancellationToken);
    }

    public async Task<FaturamentoHistoricoArquivoDto> UploadHistoryFileAsync(
        int year,
        int month,
        UploadedFile file,
        CancellationToken cancellationToken)
    {
        ValidateHistoryPeriod(year, month, requireBoth: true);
        var clinicId = tenant.GetRequiredClinicaId();
        var stored = await storage.SaveAsync(file, cancellationToken);
        try
        {
            var entity = new FaturamentoHistoricoArquivo
            {
                ClinicaId = clinicId,
                Ano = year,
                Mes = month,
                NomeOriginal = stored.OriginalName,
                ContentType = stored.ContentType,
                TamanhoBytes = stored.SizeBytes,
                Url = stored.Url,
                DataUpload = DateTime.UtcNow
            };
            db.FaturamentoHistoricoArquivos.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return new FaturamentoHistoricoArquivoDto(
                entity.Id,
                entity.Ano,
                entity.Mes,
                entity.NomeOriginal,
                entity.ContentType,
                entity.TamanhoBytes,
                entity.DataUpload);
        }
        catch
        {
            await storage.DeleteAsync(stored.Url, CancellationToken.None);
            throw;
        }
    }

    public async Task<PrivateFileDownload> DownloadHistoryFileAsync(
        int fileId,
        CancellationToken cancellationToken)
    {
        var clinicId = tenant.GetRequiredClinicaId();
        var entity = await db.FaturamentoHistoricoArquivos.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fileId && item.ClinicaId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo do histórico não encontrado.");
        var stored = await storage.GetAsync(entity.Url, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo do histórico não encontrado.");
        return new PrivateFileDownload
        {
            Content = stored.Content,
            ContentType = entity.ContentType,
            FileName = entity.NomeOriginal
        };
    }

    public async Task DeleteHistoryFileAsync(int fileId, CancellationToken cancellationToken)
    {
        var clinicId = tenant.GetRequiredClinicaId();
        var entity = await db.FaturamentoHistoricoArquivos
            .SingleOrDefaultAsync(item => item.Id == fileId && item.ClinicaId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo do histórico não encontrado.");
        var url = entity.Url;
        db.FaturamentoHistoricoArquivos.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(url, cancellationToken);
    }

    public async Task DeleteAtendimentoAsync(
        int id,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var fileUrls = await db.AtendimentoArquivos.AsNoTracking()
            .Where(item => item.AtendimentoCirurgicoId == id)
            .Select(item => item.Url)
            .ToListAsync(cancellationToken);
        await sender.Send(new ExcluirAtendimentoCommand(id, user.Id, user.PerfilId), cancellationToken);
        foreach (var fileUrl in fileUrls)
        {
            await storage.DeleteAsync(fileUrl, cancellationToken);
        }
    }

    public async Task<AtendimentoArquivoDto> UploadAtendimentoFileAsync(
        int atendimentoId,
        UploadedFile file,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var atendimento = await db.AtendimentosCirurgicos.SingleOrDefaultAsync(item => item.Id == atendimentoId, cancellationToken)
            ?? throw new KeyNotFoundException("Atendimento nao encontrado.");
        EnsureMedicalAccess(atendimento, user, "Sem permissao para anexar arquivos ao atendimento.");

        var stored = await storage.SaveAsync(file, cancellationToken);
        var entity = new AtendimentoArquivo
        {
            ClinicaId = atendimento.ClinicaId,
            AtendimentoCirurgicoId = atendimento.Id,
            NomeOriginal = stored.OriginalName,
            ContentType = stored.ContentType,
            TamanhoBytes = stored.SizeBytes,
            Url = stored.Url,
            DataUpload = DateTime.UtcNow
        };
        db.AtendimentoArquivos.Add(entity);
        atendimento.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new AtendimentoArquivoDto(entity.Id, entity.NomeOriginal, entity.ContentType,
            entity.TamanhoBytes, entity.Url, entity.DataUpload);
    }

    public async Task<PrivateFileDownload> DownloadAtendimentoFileAsync(
        int atendimentoId,
        int fileId,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var entity = await db.AtendimentoArquivos.AsNoTracking()
            .Include(item => item.AtendimentoCirurgico)
            .SingleOrDefaultAsync(item => item.Id == fileId && item.AtendimentoCirurgicoId == atendimentoId, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo nao encontrado.");
        EnsureMedicalAccess(entity.AtendimentoCirurgico, user, "Sem permissao para baixar este arquivo.");
        var stored = await storage.GetAsync(entity.Url, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo nao encontrado.");
        return new PrivateFileDownload { Content = stored.Content, ContentType = entity.ContentType, FileName = entity.NomeOriginal };
    }

    public async Task DeleteAtendimentoFileAsync(
        int atendimentoId,
        int fileId,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        var entity = await db.AtendimentoArquivos.Include(item => item.AtendimentoCirurgico)
            .SingleOrDefaultAsync(item => item.Id == fileId && item.AtendimentoCirurgicoId == atendimentoId, cancellationToken)
            ?? throw new KeyNotFoundException("Arquivo nao encontrado.");
        EnsureMedicalAccess(entity.AtendimentoCirurgico, user, "Sem permissao para excluir este arquivo.");
        var url = entity.Url;
        db.AtendimentoArquivos.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(url, cancellationToken);
    }

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

    public async Task<PagedResult<FinanceAuditItemDto>> ListAuditAsync(
        int page,
        int pageSize,
        string? resource,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new InvalidOperationException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
        var clinicId = tenant.GetRequiredClinicaId();
        var query = db.AuditoriasPlataforma.AsNoTracking()
            .Where(item => item.ClinicaId == clinicId && item.Recurso.StartsWith("financeiro:"));
        if (!string.IsNullOrWhiteSpace(resource))
            query = query.Where(item => item.Recurso == $"financeiro:{resource.Trim()}");
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.DataCadastro)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new FinanceAuditItemDto(item.Id, item.Acao, item.Recurso, item.EntidadeId,
                item.DetalhesJson, item.UserId, item.Ip, item.Sucesso, item.DataCadastro))
            .ToListAsync(cancellationToken);
        return new PagedResult<FinanceAuditItemDto>(items, page, pageSize, total);
    }

    private static void EnsureMedicalAccess(AtendimentoCirurgico atendimento, CurrentUserContext user, string message)
    {
        if (user.PerfilId == Perfil.MedicosId
            && atendimento.MedicoResponsavelId != user.Id
            && atendimento.MedicoAuxiliar1Id != user.Id
            && atendimento.MedicoAuxiliar2Id != user.Id)
            throw new UnauthorizedAccessException(message);
    }

    private static void ValidateHistoryPeriod(int? year, int? month, bool requireBoth)
    {
        if (requireBoth && (!year.HasValue || !month.HasValue))
            throw new InvalidOperationException("Ano e mês são obrigatórios.");
        if (year.HasValue && year is < 1900 or > 2100)
            throw new InvalidOperationException("O ano deve estar entre 1900 e 2100.");
        if (month.HasValue && month is < 1 or > 12)
            throw new InvalidOperationException("O mês deve estar entre 1 e 12.");
        if (month.HasValue && !year.HasValue)
            throw new InvalidOperationException("Informe o ano ao filtrar por mês.");
    }
}

public sealed record ReceiptUploadResponse(int RecebimentoId, string Nome, string ContentType, long Tamanho, string Url);
public sealed record FinanceAuditItemDto(long Id, string Acao, string Recurso, string? EntidadeId,
    string? DetalhesJson, int? UserId, string? Ip, bool Sucesso, DateTime DataCadastro);
