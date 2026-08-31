using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed partial class FinanceiroFileUseCases(
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

}
