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

}
