using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

public class CreatePacienteObservacaoCommandHandler : IRequestHandler<CreatePacienteObservacaoCommand, CreatePacienteObservacaoResult>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<CreatePacienteObservacaoCommandHandler> _logger;

    public CreatePacienteObservacaoCommandHandler(
        IAppDbContext context,
        ILogger<CreatePacienteObservacaoCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CreatePacienteObservacaoResult> Handle(CreatePacienteObservacaoCommand request, CancellationToken cancellationToken)
    {
        var paciente = await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var destinatarioIds = request.ObservacaoPaiId.HasValue
            ? await ResolveReplyRecipientsAsync(request, cancellationToken)
            : await ResolveRootRecipientsAsync(request, paciente, cancellationToken);

        if (destinatarioIds.Count == 0)
        {
            throw new InvalidOperationException("Nao foi encontrado nenhum destinatario para a observacao.");
        }

        var observacoes = destinatarioIds
            .Distinct()
            .Where(userId => userId != request.CurrentUserId)
            .Select(destinatarioId => new Observacao
            {
                PacienteId = paciente.Id,
                AutorUserId = request.CurrentUserId,
                DestinatarioUserId = destinatarioId,
                ObservacaoPaiId = request.ObservacaoPaiId,
                Texto = request.Texto.Trim(),
                MedicoUserId = paciente.MedicoUserId,
                Medico = paciente.Medico,
                MedicoAuxiliar1UserId = paciente.MedicoAuxiliar1UserId,
                MedicoAuxiliar1 = paciente.MedicoAuxiliar1,
                MedicoAuxiliar2UserId = paciente.MedicoAuxiliar2UserId,
                MedicoAuxiliar2 = paciente.MedicoAuxiliar2
            })
            .ToList();

        if (observacoes.Count == 0)
        {
            throw new InvalidOperationException("Nao foi encontrado nenhum destinatario valido para a observacao.");
        }

        _context.Observacoes.AddRange(observacoes);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreatePacienteObservacaoResult
        {
            PacienteId = paciente.Id,
            CreatedCount = observacoes.Count
        };
    }

    private async Task<List<int>> ResolveReplyRecipientsAsync(CreatePacienteObservacaoCommand request, CancellationToken cancellationToken)
    {
        var parent = await _context.Observacoes
            .AsNoTracking()
            .Where(observacao => observacao.Id == request.ObservacaoPaiId && observacao.PacienteId == request.PacienteId)
            .Select(observacao => new
            {
                observacao.Id,
                observacao.AutorUserId,
                observacao.DestinatarioUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (parent == null)
        {
            throw new InvalidOperationException("Observacao de origem nao encontrada.");
        }

        if (parent.AutorUserId == request.CurrentUserId)
        {
            return [parent.DestinatarioUserId];
        }

        if (parent.DestinatarioUserId == request.CurrentUserId)
        {
            return [parent.AutorUserId];
        }

        throw new UnauthorizedAccessException("Sem permissao para responder esta observacao.");
    }

    private async Task<List<int>> ResolveRootRecipientsAsync(
        CreatePacienteObservacaoCommand request,
        PacienteObservacaoContext paciente,
        CancellationToken cancellationToken)
    {
        if (request.CurrentPerfilId == Perfil.AdministradorId || request.CurrentPerfilId == Perfil.ControllerId)
        {
            var medicalIds = new[] { paciente.MedicoUserId, paciente.MedicoAuxiliar1UserId, paciente.MedicoAuxiliar2UserId }
                .Where(userId => userId.HasValue)
                .Select(userId => userId!.Value)
                .Distinct()
                .ToList();

            if (medicalIds.Count == 0)
            {
                throw new InvalidOperationException("Selecione ao menos um medico vinculado ao paciente antes de enviar observacoes.");
            }

            return await _context.Users
                .AsNoTracking()
                .Where(user => medicalIds.Contains(user.Id) && user.Ativo)
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        if (request.CurrentPerfilId == Perfil.MedicosId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(user =>
                    user.Ativo
                    && (user.PerfilId == Perfil.AdministradorId || user.PerfilId == Perfil.ControllerId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        throw new UnauthorizedAccessException("Sem permissao para registrar observacoes.");
    }
}

public class GetPacienteObservacoesQueryHandler : IRequestHandler<GetPacienteObservacoesQuery, IReadOnlyList<PacienteObservacaoDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetPacienteObservacoesQueryHandler> _logger;

    public GetPacienteObservacoesQueryHandler(
        IAppDbContext context,
        ILogger<GetPacienteObservacoesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PacienteObservacaoDto>> Handle(GetPacienteObservacoesQuery request, CancellationToken cancellationToken)
    {
        await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        try
        {
            return await _context.Observacoes
                .AsNoTracking()
                .Where(observacao =>
                    observacao.PacienteId == request.PacienteId
                    && (observacao.AutorUserId == request.CurrentUserId || observacao.DestinatarioUserId == request.CurrentUserId))
                .OrderByDescending(observacao => observacao.DataCadastro)
                .ThenByDescending(observacao => observacao.Id)
                .Select(observacao => new PacienteObservacaoDto
                {
                    Id = observacao.Id,
                    PacienteId = observacao.PacienteId,
                    ObservacaoPaiId = observacao.ObservacaoPaiId,
                    Texto = observacao.Texto,
                    DataCadastro = observacao.DataCadastro,
                    DataLeitura = observacao.DataLeitura,
                    AutorUserId = observacao.AutorUserId,
                    AutorNome = observacao.AutorUser.Nome,
                    AutorPerfilId = observacao.AutorUser.PerfilId,
                    AutorPerfilNome = observacao.AutorUser.Perfil.Nome,
                    DestinatarioUserId = observacao.DestinatarioUserId,
                    DestinatarioNome = observacao.DestinatarioUser.Nome,
                    DestinatarioPerfilId = observacao.DestinatarioUser.PerfilId,
                    DestinatarioPerfilNome = observacao.DestinatarioUser.Perfil.Nome,
                    NomePaciente = observacao.Paciente.NomePaciente,
                    MedicoUserId = observacao.MedicoUserId,
                    Medico = observacao.Medico,
                    MedicoAuxiliar1UserId = observacao.MedicoAuxiliar1UserId,
                    MedicoAuxiliar1 = observacao.MedicoAuxiliar1,
                    MedicoAuxiliar2UserId = observacao.MedicoAuxiliar2UserId,
                    MedicoAuxiliar2 = observacao.MedicoAuxiliar2,
                    FoiLida = observacao.DataLeitura.HasValue,
                    EnviadaPorMim = observacao.AutorUserId == request.CurrentUserId
                })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar observacoes do paciente {PacienteId}", request.PacienteId);
            throw;
        }
    }
}

public class MarkPacienteObservacoesAsReadCommandHandler : IRequestHandler<MarkPacienteObservacoesAsReadCommand, MarkPacienteObservacoesAsReadResult>
{
    private readonly IAppDbContext _context;

    public MarkPacienteObservacoesAsReadCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<MarkPacienteObservacoesAsReadResult> Handle(MarkPacienteObservacoesAsReadCommand request, CancellationToken cancellationToken)
    {
        await PacienteObservacaoAccess.GetPacienteContextAsync(
            _context,
            request.PacienteId,
            request.CurrentPerfilId,
            request.CurrentUserId,
            cancellationToken);

        var unread = await _context.Observacoes
            .Where(observacao =>
                observacao.PacienteId == request.PacienteId
                && observacao.DestinatarioUserId == request.CurrentUserId
                && observacao.DataLeitura == null)
            .ToListAsync(cancellationToken);

        if (unread.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var observacao in unread)
            {
                observacao.DataLeitura = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new MarkPacienteObservacoesAsReadResult
        {
            PacienteId = request.PacienteId,
            UpdatedCount = unread.Count
        };
    }
}

internal static class PacienteObservacaoAccess
{
    public static async Task<PacienteObservacaoContext> GetPacienteContextAsync(
        IAppDbContext context,
        int pacienteId,
        int currentPerfilId,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var query = PacienteAccess.ApplyScope(
            context,
            context.Pacientes.AsNoTracking(),
            currentPerfilId,
            currentUserId);

        var paciente = await query
            .Where(item => item.Id == pacienteId)
            .Select(item => new PacienteObservacaoContext(
                item.Id,
                item.NomePaciente,
                item.MedicoUserId,
                item.MedicoUser != null ? item.MedicoUser.Nome : item.Medico,
                item.MedicoAuxiliar1UserId,
                item.MedicoAuxiliar1User != null ? item.MedicoAuxiliar1User.Nome : item.MedicoAuxiliar1,
                item.MedicoAuxiliar2UserId,
                item.MedicoAuxiliar2User != null ? item.MedicoAuxiliar2User.Nome : item.MedicoAuxiliar2))
            .FirstOrDefaultAsync(cancellationToken);

        if (paciente == null)
        {
            throw new UnauthorizedAccessException("Sem permissao para acessar as observacoes deste paciente.");
        }

        return paciente;
    }
}

internal sealed record PacienteObservacaoContext(
    int Id,
    string NomePaciente,
    int? MedicoUserId,
    string? Medico,
    int? MedicoAuxiliar1UserId,
    string? MedicoAuxiliar1,
    int? MedicoAuxiliar2UserId,
    string? MedicoAuxiliar2);
