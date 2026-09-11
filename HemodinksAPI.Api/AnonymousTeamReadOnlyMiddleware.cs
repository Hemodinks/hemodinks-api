using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Api;

public sealed class AnonymousTeamReadOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isAnonymousTeam = context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst("perfilId")?.Value == Perfil.EquipeId.ToString()
            && context.User.FindFirst(GlobalIdentityClaimTypes.EquipeOperadorId) == null;
        var method = context.Request.Method;
        var isMutation = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
        var endpointName = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        // Preserve existing consent/privacy and credential recovery flows. No business writes.
        var isAccountAction = endpointName is "AcceptCurrentLegalDocuments" or "UpdateCurrentPrivacyPreference"
            or "ChangePassword" or "ChangeTemporaryPassword";
        if (isAnonymousTeam && isMutation && !isAccountAction)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Acesso somente leitura." }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
