using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed partial class FinanceiroFileUseCases
{
    public async Task DeleteAtendimentoAsync(
        int id,
        CurrentUserContext user,
        string? motivo,
        CancellationToken cancellationToken)
    {
        await EnsurePacienteAtivoAsync(id, user, cancellationToken);
        // Cancelamento logico: arquivos do atendimento ficam no storage (guarda do prontuario).
        await sender.Send(new ExcluirAtendimentoCommand(id, user.Id, user.PerfilId, motivo), cancellationToken);
    }

    public async Task<AtendimentoArquivoDto> UploadAtendimentoFileAsync(
        int atendimentoId,
        UploadedFile file,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        await EnsurePacienteAtivoAsync(atendimentoId, user, cancellationToken);
        var atendimento = await db.AtendimentosCirurgicos.SingleOrDefaultAsync(item => item.Id == atendimentoId, cancellationToken)
            ?? throw new ResourceNotFoundException("Atendimento nao encontrado.");
        EnsureMedicalAccess(atendimento, user, "Sem permissao para anexar arquivos ao atendimento.");
        if (atendimento.CanceladoEm.HasValue)
            throw new BusinessRuleException("Atendimento cancelado nao recebe novos arquivos.");

        var stored = await storage.SaveAsync(file, atendimento.ClinicaId, cancellationToken);
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
        await EnsurePacienteAtivoAsync(atendimentoId, user, cancellationToken);
        var entity = await db.AtendimentoArquivos.AsNoTracking()
            .Include(item => item.AtendimentoCirurgico)
            .SingleOrDefaultAsync(item => item.Id == fileId && item.AtendimentoCirurgicoId == atendimentoId
                && item.ExcluidoEm == null, cancellationToken)
            ?? throw new ResourceNotFoundException("Arquivo nao encontrado.");
        EnsureMedicalAccess(entity.AtendimentoCirurgico, user, "Sem permissao para baixar este arquivo.");
        var stored = await storage.GetAsync(entity.Url, entity.ClinicaId, cancellationToken)
            ?? throw new StoredFileUnavailableException(
                "Arquivo do atendimento registrado nao foi localizado no armazenamento.");
        return new PrivateFileDownload { Content = stored.Content, ContentType = entity.ContentType, FileName = entity.NomeOriginal };
    }

    public async Task DeleteAtendimentoFileAsync(
        int atendimentoId,
        int fileId,
        CurrentUserContext user,
        CancellationToken cancellationToken)
    {
        await EnsurePacienteAtivoAsync(atendimentoId, user, cancellationToken);
        var entity = await db.AtendimentoArquivos.Include(item => item.AtendimentoCirurgico)
            .SingleOrDefaultAsync(item => item.Id == fileId && item.AtendimentoCirurgicoId == atendimentoId
                && item.ExcluidoEm == null, cancellationToken)
            ?? throw new ResourceNotFoundException("Arquivo nao encontrado.");
        EnsureMedicalAccess(entity.AtendimentoCirurgico, user, "Sem permissao para excluir este arquivo.");
        // Exclusao logica: linha e storage preservados (guarda do prontuario); o arquivo some da aplicacao.
        var now = DateTime.UtcNow;
        entity.ExcluidoEm = now;
        entity.ExcluidoPorUserId = user.Id;
        entity.AtendimentoCirurgico.DataAtualizacao = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    // #369: atendimento e arquivos de paciente inativado so para o Administrador (resposta igual a inexistente).
    private async Task EnsurePacienteAtivoAsync(int atendimentoId, CurrentUserContext user, CancellationToken cancellationToken)
    {
        if (Perfil.IsAdministradorOuSuper(user.PerfilId))
            return;
        if (await db.AtendimentosCirurgicos.AnyAsync(item => item.Id == atendimentoId && !item.Paciente.Ativo, cancellationToken))
            throw new ResourceNotFoundException("Atendimento nao encontrado.");
    }
}
