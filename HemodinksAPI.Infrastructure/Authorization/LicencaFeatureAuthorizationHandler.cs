using HemodinksAPI.Application.Features.Licencas;
using Microsoft.AspNetCore.Authorization;

namespace HemodinksAPI.Infrastructure.Authorization;

public sealed class LicencaFeatureRequirement : IAuthorizationRequirement
{
    public LicencaFeatureRequirement(string feature)
    {
        Feature = feature;
    }

    public string Feature { get; }
}

public sealed class LicencaFeatureAuthorizationHandler
    : AuthorizationHandler<LicencaFeatureRequirement>
{
    private readonly ILicencaService _licencaService;

    public LicencaFeatureAuthorizationHandler(ILicencaService licencaService)
    {
        _licencaService = licencaService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        LicencaFeatureRequirement requirement)
    {
        var currentUser = context.User.ToCurrentUserContext();
        if (currentUser == null)
        {
            return;
        }

        if (await _licencaService.HasFeatureAsync(currentUser, requirement.Feature, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
