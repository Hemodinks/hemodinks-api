using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Authentication;

namespace HemodinksAPI.Api;

/// <summary>
/// #368: registra em AuditoriasClinica quem acessou ou alterou dado de paciente. Guarda so ids, acao,
/// status e IP truncado; nunca query string, payload, nome, CPF ou conteudo clinico. Falha ao gravar
/// e so registrada em log (tipo do erro), sem afetar nem detalhar a resposta ao cliente.
/// </summary>
internal sealed class ClinicAuditEndpointFilter(
    string action,
    Func<EndpointFilterInvocationContext, string?> resolveResource) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var resource = resolveResource(context);
        if (resource is null)
            return await next(context);

        object? result;
        try
        {
            result = await next(context);
        }
        catch (Exception exception)
        {
            // Mesmo mapeamento do handler global (ApiExceptionHandling): a trilha guarda o status que o cliente recebe.
            var mapped = ApiExceptionResults.Map(exception, new EndpointErrorOptions { NotFoundUsesExceptionMessage = true });
            await TryRecordAsync(context.HttpContext, resource, null, StatusOf(mapped) is var mappedStatus
                && mappedStatus != StatusCodes.Status200OK ? mappedStatus : StatusCodes.Status500InternalServerError);
            throw;
        }

        await TryRecordAsync(context.HttpContext, resource, result, StatusOf(result));
        return result;
    }

    // Forbid/Challenge nao expoem StatusCode: viram 403/401 no esquema de autenticacao.
    private static int StatusOf(object? result) => result switch
    {
        IStatusCodeHttpResult { StatusCode: int statusCode } => statusCode,
        Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult => StatusCodes.Status403Forbidden,
        Microsoft.AspNetCore.Http.HttpResults.ChallengeHttpResult => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status200OK
    };

    private async Task TryRecordAsync(HttpContext http, string resource, object? result, int statusCode)
    {
        try
        {
            var writer = http.RequestServices.GetRequiredService<IClinicAuditWriter>();
            var timeProvider = http.RequestServices.GetRequiredService<TimeProvider>();
            // Sem o token da requisicao: cliente que desconecta nao apaga o rastro do acesso.
            await writer.WriteAsync(new ClinicAuditEntry(
                ParseClaim(http.User, ClaimTypes.NameIdentifier),
                ParseClaim(http.User, GlobalIdentityClaimTypes.EquipeOperadorId),
                action,
                resource,
                ResolveResourceId(http, result),
                statusCode,
                TruncateIp(http.Connection.RemoteIpAddress),
                timeProvider.GetUtcNow().UtcDateTime), CancellationToken.None);
        }
        catch (Exception exception)
        {
            http.RequestServices.GetRequiredService<ILogger<ClinicAuditEndpointFilter>>()
                .LogError("Falha ao registrar auditoria clinica {Acao}: {ErrorType}", action, exception.GetType().Name);
        }
    }

    private static int? ParseClaim(ClaimsPrincipal user, string type) =>
        int.TryParse(user.FindFirstValue(type), out var value) ? value : null;

    private static string? ResolveResourceId(HttpContext http, object? result)
    {
        if (http.Request.RouteValues.TryGetValue("id", out var routeId) && routeId is not null)
        {
            // So id inteiro (int): texto livre da rota, ou um CPF de 11 digitos, nunca vai para a trilha.
            return int.TryParse(routeId.ToString(), out var parsedId) ? parsedId.ToString() : null;
        }

        // Criacao: o id so existe no resultado (ex.: Results.Created com o DTO criado).
        if (result is IValueHttpResult { Value: { } value })
        {
            var id = value.GetType().GetProperty("Id")?.GetValue(value);
            if (id is int or long)
                return id.ToString();
        }

        return null;
    }

    internal static string? TruncateIp(IPAddress? address)
    {
        if (address is null)
            return null;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        var keep = address.AddressFamily == AddressFamily.InterNetwork ? 3 : 6; // /24 IPv4, /48 IPv6
        for (var index = keep; index < bytes.Length; index++)
            bytes[index] = 0;
        return new IPAddress(bytes).ToString();
    }
}

internal static class ClinicAuditEndpointExtensions
{
    public const string PacienteResource = "paciente";

    public static RouteHandlerBuilder WithPacienteAudit(this RouteHandlerBuilder builder, string action) =>
        builder.AddEndpointFilter(new ClinicAuditEndpointFilter(action, _ => PacienteResource));

    public static RouteHandlerBuilder WithClinicAudit(this RouteHandlerBuilder builder, string action,
        Func<EndpointFilterInvocationContext, string?> resolveResource) =>
        builder.AddEndpointFilter(new ClinicAuditEndpointFilter(action, resolveResource));
}
