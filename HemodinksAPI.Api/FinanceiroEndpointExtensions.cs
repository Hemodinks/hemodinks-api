using System.Security.Claims;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Financeiro;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api;

public static class FinanceiroEndpointExtensions
{
    public static void MapFinanceiroEndpoints(this WebApplication app)
    {
        var atendimentos = app.MapGroup("/api/atendimentos-cirurgicos").WithTags("Atendimentos cirurgicos").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter());
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

        var faturamentos = app.MapGroup("/api/faturamentos").WithTags("Faturamento").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter());
        faturamentos.MapGet("/", async (ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
        {
            var user = principal.ToCurrentUserContext() ?? throw new UnauthorizedAccessException();
            return Results.Ok(await mediator.Send(new ListarFaturamentosQuery(user.Id, user.PerfilId), ct));
        }).RequireAuthorization("FaturamentoVisualizar");
        faturamentos.MapPost("/", async (CriarFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct); return Results.Created($"/api/faturamentos/{result.Id}", result);
        }).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPut("/{id:int}/status", async (int id, AtualizarStatusFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/retorno", async (int id, RegistrarRetornoFaturamentoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/glosas", async (int id, RegistrarGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { FaturamentoId = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/glosas/{id:int}/recursos", async (int id, RegistrarRecursoGlosaCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { GlosaId = id }, ct))).RequireAuthorization("FaturamentoGerenciar");
        faturamentos.MapPost("/{id:int}/contas-receber", async (int id, GerarContaReceberCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { FaturamentoId = id }, ct))).RequireAuthorization("FinanceiroGerenciar");

        var financeiro = app.MapGroup("/api/financeiro/contas-receber").WithTags("Contas a receber").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter());
        financeiro.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarContasReceberQuery(), ct))).RequireAuthorization("FinanceiroVisualizar");
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

        var precos = app.MapGroup("/api/convenios-procedimentos-precos").WithTags("Precos por convenio").RequireAuthorization()
            .AddEndpointFilter(new FinanceiroExceptionFilter());
        precos.MapGet("/", async (int? convenioId, string? cbhpmCodigo, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarConvenioProcedimentoPrecosQuery(convenioId, cbhpmCodigo), ct)))
            .RequireAuthorization("TabelaPrecoVisualizar");
        precos.MapPost("/", async (SalvarConvenioProcedimentoPrecoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command, ct))).RequireAuthorization("TabelaPrecoGerenciar");
        precos.MapPut("/{id:int}", async (int id, SalvarConvenioProcedimentoPrecoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(command with { Id = id }, ct))).RequireAuthorization("TabelaPrecoGerenciar");
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
