using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Financeiro;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static class FinanceiroEndpointExtensions
{
    public static void MapFinanceiroEndpoints(this WebApplication app)
    {
        var atendimentos = app.MapGroup("/api/atendimentos-cirurgicos").WithTags("Atendimentos cirurgicos").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter()).AddEndpointFilter<FinanceiroAuditFilter>();
        atendimentos.MapGet("/", async (int? pacienteId, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ListarAtendimentosQuery(pacienteId, user.Id, user.PerfilId), ct));
        })
            .RequireAuthorization("AtendimentoVisualizar");
        atendimentos.MapPost("/", async (CriarAtendimentoCommand command, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            command = command with { CurrentUserId = user.Id, CurrentPerfilId = user.PerfilId };
            var result = await mediator.Send(command, ct);
            return Results.Created($"/api/atendimentos-cirurgicos/{result.Id}", result);
        }).RequireAuthorization("AtendimentoGerenciar");
        atendimentos.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ObterAtendimentoQuery(id, user.Id, user.PerfilId), ct));
        }).RequireAuthorization("AtendimentoVisualizar");
        atendimentos.MapPut("/{id:int}", async (int id, AtualizarAtendimentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("AtendimentoGerenciar");
        atendimentos.MapDelete("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ExcluirAtendimentoCommand(id), ct); return Results.NoContent();
        }).RequireAuthorization("AtendimentoGerenciar");

        var faturamentos = app.MapGroup("/api/faturamentos").WithTags("Faturamento").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter()).AddEndpointFilter<FinanceiroAuditFilter>();
        faturamentos.MapGet("/", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ListarFaturamentosQuery(user.Id, user.PerfilId), ct));
        }).RequireAuthorization("FaturamentoVisualizar");
        faturamentos.MapPost("/", async (CriarFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct); return Results.Created($"/api/faturamentos/{result.Id}", result);
        }).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ObterFaturamentoQuery(id, user.Id, user.PerfilId), ct));
        }).RequireAuthorization("FaturamentoVisualizar");
        faturamentos.MapGet("/pesquisa", async (int page, int pageSize, string? termo, FaturamentoStatus? status,
            DateTime? competenciaInicio, DateTime? competenciaFim, int? convenioId, ClaimsPrincipal principal,
            IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new PesquisarFaturamentosQuery(page, pageSize, termo, status,
                competenciaInicio, competenciaFim, convenioId, user.Id, user.PerfilId), ct));
        }).RequireAuthorization("FaturamentoVisualizar");
        faturamentos.MapPut("/{id:int}", async (int id, AtualizarFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPut("/{id:int}/itens/{itemId:int}", async (int id, int itemId,
            AtualizarFaturamentoItemCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { FaturamentoId = id, ItemId = itemId }, ct)))
            .RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapDelete("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ExcluirFaturamentoCommand(id), ct); return Results.NoContent();
        }).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPut("/{id:int}/status", async (int id, AtualizarStatusFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/retorno", async (int id, RegistrarRetornoFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/glosas", async (int id, RegistrarGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { FaturamentoId = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/glosas/{id:int}/recursos", async (int id, RegistrarRecursoGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { GlosaId = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPut("/glosas/{id:int}", async (int id, AtualizarGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapDelete("/glosas/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ExcluirGlosaCommand(id), ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPut("/recursos-glosa/{id:int}", async (int id, AtualizarRecursoGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapDelete("/recursos-glosa/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ExcluirRecursoGlosaCommand(id), ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/contas-receber", async (int id, GerarContaReceberCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { FaturamentoId = id }, ct))).RequireAuthorization("FinanceiroGerenciar");

        var financeiro = app.MapGroup("/api/financeiro/contas-receber").WithTags("Contas a receber").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter()).AddEndpointFilter<FinanceiroAuditFilter>();
        financeiro.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarContasReceberQuery(), ct))).RequireAuthorization("FinanceiroVisualizar");
        financeiro.MapGet("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObterContaReceberQuery(id), ct))).RequireAuthorization("FinanceiroVisualizar");
        financeiro.MapGet("/pesquisa", async (int page, int pageSize, string? termo, ContaReceberStatus? status,
            DateTime? vencimentoInicio, DateTime? vencimentoFim, int? convenioId, int? medicoId, int? pacienteId,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new PesquisarContasReceberQuery(page, pageSize, termo, status,
                vencimentoInicio, vencimentoFim, convenioId, medicoId, pacienteId), ct))).RequireAuthorization("FinanceiroVisualizar");
        financeiro.MapPut("/{id:int}", async (int id, AtualizarContaReceberCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FinanceiroGerenciar");
        financeiro.MapPost("/{id:int}/cancelamento", async (int id, CancelarContaReceberCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FinanceiroGerenciar");
        financeiro.MapPost("/{id:int}/recebimentos", async (int id, RegistrarRecebimentoCommand command,
            ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(command with { ContaReceberId = id, UsuarioCadastroId = user.Id }, ct));
        }).RequireAuthorization("FinanceiroGerenciar");
        financeiro.MapPost("/recebimentos/{id:int}/estorno", async (int id, EstornarRecebimentoCommand command,
            ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(command with { RecebimentoId = id, UsuarioEstornoId = user.Id }, ct));
        }).RequireAuthorization("FinanceiroGerenciar");
        financeiro.MapPost("/recebimentos/{id:int}/comprovante", async (int id, IFormFile arquivo,
            IPatientFileStorage storage, AppDbContext db, CancellationToken ct) =>
        {
            var extension = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
            if (extension is not ".pdf" and not ".jpg" and not ".jpeg")
                throw new InvalidOperationException("O comprovante deve estar no formato PDF ou JPG.");

            var receipt = await db.Recebimentos.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Recebimento nao encontrado.");
            var oldUrl = receipt.DocumentoComprovante;
            var stored = await storage.SaveAsync(arquivo, ct);
            receipt.DocumentoComprovante = stored.Url; await db.SaveChangesAsync(ct);
            if (!string.IsNullOrWhiteSpace(oldUrl)) await storage.DeleteAsync(oldUrl, ct);
            return Results.Ok(new { recebimentoId = id, nome = stored.OriginalName, contentType = stored.ContentType,
                tamanho = stored.SizeBytes, url = stored.Url });
        }).DisableAntiforgery().RequireAuthorization("FinanceiroGerenciar");
        financeiro.MapGet("/recebimentos/{id:int}/comprovante", async (int id,
            IPatientFileStorage storage, AppDbContext db, CancellationToken ct) =>
        {
            var receipt = await db.Recebimentos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Recebimento nao encontrado.");
            var storedUrl = receipt.DocumentoComprovante
                ?? throw new KeyNotFoundException("Comprovante nao encontrado.");
            var file = await storage.GetAsync(storedUrl, ct)
                ?? throw new KeyNotFoundException("Comprovante nao encontrado.");
            var storedPath = Uri.TryCreate(storedUrl, UriKind.Absolute, out var storedUri)
                ? storedUri.AbsolutePath
                : storedUrl;
            var extension = Path.GetExtension(storedPath).ToLowerInvariant();
            var (contentType, downloadExtension) = extension switch
            {
                ".pdf" => ("application/pdf", ".pdf"),
                ".jpg" or ".jpeg" => ("image/jpeg", ".jpg"),
                _ => throw new InvalidOperationException("O formato do comprovante armazenado não é suportado.")
            };
            return Results.Stream(file.Content, contentType, $"comprovante-{id}{downloadExtension}");
        }).RequireAuthorization("FinanceiroVisualizar");

        var precos = app.MapGroup("/api/convenios-procedimentos-precos").WithTags("Precos por convenio").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter()).AddEndpointFilter<FinanceiroAuditFilter>();
        precos.MapGet("/", async (int? convenioId, string? cbhpmCodigo, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarConvenioProcedimentoPrecosQuery(convenioId, cbhpmCodigo), ct)))
            .RequireAuthorization("TabelaPrecoVisualizar");
        precos.MapPost("/", async (SalvarConvenioProcedimentoPrecoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command, ct))).RequireAuthorization("TabelaPrecoGerenciar");
        precos.MapPut("/{id:int}", async (int id, SalvarConvenioProcedimentoPrecoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("TabelaPrecoGerenciar");
        precos.MapDelete("/{id:int}", async (int id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ExcluirConvenioProcedimentoPrecoCommand(id), ct); return Results.NoContent();
        }).RequireAuthorization("TabelaPrecoGerenciar");

        var reports = app.MapGroup("/api/financeiro").WithTags("Relatorios financeiros").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter());
        reports.MapGet("/relatorios/resumo", async (DateTime? inicio, DateTime? fim, int? convenioId, int? medicoId,
            int? pacienteId,
            IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(
                new ObterFinanceiroResumoQuery(inicio, fim, convenioId, medicoId, pacienteId), ct))).RequireAuthorization("FinanceiroVisualizar");
        reports.MapGet("/auditoria", async (int page, int pageSize, string? recurso, AppDbContext db,
            IClinicaContext tenant, CancellationToken ct) =>
        {
            if (page < 1 || pageSize is < 1 or > 100)
                throw new InvalidOperationException("Pagina deve ser positiva e o tamanho deve estar entre 1 e 100.");
            var clinicaId = tenant.GetRequiredClinicaId();
            var query = db.AuditoriasPlataforma.AsNoTracking().Where(x => x.ClinicaId == clinicaId && x.Recurso.StartsWith("financeiro:"));
            if (!string.IsNullOrWhiteSpace(recurso)) query = query.Where(x => x.Recurso == $"financeiro:{recurso.Trim()}");
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.DataCadastro).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new { x.Id, x.Acao, x.Recurso, x.EntidadeId, x.DetalhesJson, x.UserId, x.Ip, x.Sucesso, x.DataCadastro }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), page, pageSize, total));
        }).RequireAuthorization("FinanceiroVisualizar");

        app.MapGet("/api/pacientes/{id:int}/resumo-financeiro", async (int id, ClaimsPrincipal principal,
            IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ObterPacienteFinanceiroResumoQuery(id, user.Id, user.PerfilId), ct));
        }).WithTags("Pacientes").RequireAuthorization(LicencaPolicies.PacientesVisualizar).AddEndpointFilter(new FinanceiroExceptionFilter());
    }
}

internal sealed class FinanceiroAuditFilter(PlatformAuditService audit, IClinicaContext tenant,
    ILogger<FinanceiroAuditFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) return result;
        try
        {
            var route = http.Request.Path.Value?.Trim('/').Replace("api/", string.Empty) ?? "operacao";
            var resource = route.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "operacao";
            var entityId = http.Request.RouteValues.TryGetValue("id", out var id) ? id?.ToString() : null;
            var command = context.Arguments.FirstOrDefault(argument => argument?.GetType().Namespace == "HemodinksAPI.Application.Features.Financeiro"
                && argument.GetType().Name.EndsWith("Command", StringComparison.Ordinal));
            await audit.RecordAsync(http, http.Request.Method, $"financeiro:{resource}", entityId, tenant.ClinicaId,
                new { rota = http.Request.Path.Value, requestId = http.TraceIdentifier, alteracao = command }, true, http.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao registrar auditoria financeira da requisicao {RequestId}", http.TraceIdentifier);
        }
        return result;
    }
}

internal sealed class FinanceiroExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { message = ex.Message }); }
        catch (DbUpdateConcurrencyException ex) { return Results.Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
    }
}
