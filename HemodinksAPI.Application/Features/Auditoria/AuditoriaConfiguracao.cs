using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Features.ConfiguracoesSistema;
using MediatR;

namespace HemodinksAPI.Application.Features.Auditoria;

// Opcoes -> Configuracoes: liga/desliga a auditoria de alteracoes de pacientes e atendimentos da clinica ativa.
public sealed record AuditoriaConfiguracaoDto(bool AuditarAtendimentos);

public sealed record ObterAuditoriaConfiguracaoQuery : IRequest<AuditoriaConfiguracaoDto>;

public sealed record AtualizarAuditoriaConfiguracaoCommand(bool AuditarAtendimentos) : IRequest<AuditoriaConfiguracaoDto>;

public sealed class AuditoriaConfiguracaoHandlers(IConfiguracaoSistemaRepository repository, TimeProvider timeProvider)
    : IRequestHandler<ObterAuditoriaConfiguracaoQuery, AuditoriaConfiguracaoDto>,
      IRequestHandler<AtualizarAuditoriaConfiguracaoCommand, AuditoriaConfiguracaoDto>
{
    public async Task<AuditoriaConfiguracaoDto> Handle(ObterAuditoriaConfiguracaoQuery request, CancellationToken ct)
    {
        var configuracao = await repository.GetCurrentOrCreateAsync(ct);
        return new AuditoriaConfiguracaoDto(configuracao.AuditarAtendimentos);
    }

    public async Task<AuditoriaConfiguracaoDto> Handle(AtualizarAuditoriaConfiguracaoCommand request, CancellationToken ct)
    {
        // Repositorio sob o filtro de tenant: so a configuracao da clinica ativa e alterada.
        var configuracao = await repository.GetCurrentOrCreateAsync(ct);
        configuracao.AuditarAtendimentos = request.AuditarAtendimentos;
        configuracao.DataAtualizacao = timeProvider.GetUtcNow().UtcDateTime;
        await repository.SaveChangesAsync(ct);
        return new AuditoriaConfiguracaoDto(configuracao.AuditarAtendimentos);
    }
}
