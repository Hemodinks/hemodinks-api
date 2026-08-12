using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public sealed class GetAvailableProfilesQueryHandler
    : IRequestHandler<GetAvailableProfilesQuery, IReadOnlyList<UserProfileOptionDto>>
{
    private readonly IAppDbContext _context;

    public GetAvailableProfilesQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserProfileOptionDto>> Handle(
        GetAvailableProfilesQuery request,
        CancellationToken cancellationToken)
    {
        var allowedProfileIds = request.CurrentUser.IsSuperAdministrador
            ? new[] { Perfil.AdministradorId, Perfil.MedicosId, Perfil.ControllerId, Perfil.SuperAdministradorId }
            : new[] { Perfil.AdministradorId, Perfil.MedicosId, Perfil.ControllerId };

        return await _context.Perfis
            .AsNoTracking()
            .Where(perfil => allowedProfileIds.Contains(perfil.Id))
            .OrderBy(perfil => perfil.Nome)
            .Select(perfil => new UserProfileOptionDto(perfil.Id, perfil.Nome))
            .ToListAsync(cancellationToken);
    }
}
