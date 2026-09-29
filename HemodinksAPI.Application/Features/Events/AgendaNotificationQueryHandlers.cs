using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public sealed class GetAgendaNotificationRecipientOptionsQueryHandler
    : IRequestHandler<GetAgendaNotificationRecipientOptionsQuery, AgendaNotificationRecipientOptionsDto>
{
    private readonly IEventFeatureDbContext _context;

    public GetAgendaNotificationRecipientOptionsQueryHandler(IEventFeatureDbContext context)
    {
        _context = context;
    }

    public async Task<AgendaNotificationRecipientOptionsDto> Handle(
        GetAgendaNotificationRecipientOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var validation = new AgendaRecipientQueryValidator().Validate(request);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Errors[0].ErrorMessage);
        var currentUser = request.CurrentUser;
        if (currentUser.IsPaciente)
        {
            throw new UnauthorizedAccessException();
        }

        var usersQuery = EventRecipientScope.AllowedUsers(_context, currentUser);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            usersQuery = usersQuery.Where(user => user.Nome.ToLower().Contains(term));
        }
        if (request.Profile == "medical") usersQuery = usersQuery.Where(user => user.PerfilId == Perfil.MedicosId);
        if (request.Profile == "administrative") usersQuery = usersQuery.Where(user =>
            user.PerfilId == Perfil.AdministradorId || user.PerfilId == Perfil.SuperAdministradorId || user.PerfilId == Perfil.ControllerId);
        var totalUsers = await usersQuery.CountAsync(cancellationToken);
        var groupsQuery = EventRecipientScope.AllowedGroups(_context, currentUser);

        var users = await usersQuery
            .OrderBy(user => user.Nome)
            .ThenBy(user => user.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(user => new AgendaNotificationRecipientUserDto
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                PerfilId = user.PerfilId,
                PerfilNome = user.Perfil.Nome
            })
            .ToListAsync(cancellationToken);

        var groups = await groupsQuery
            .OrderBy(group => group.Nome)
            .ThenBy(group => group.Id)
            .Select(group => new AgendaNotificationRecipientGroupDto
            {
                Id = group.Id,
                Nome = group.Nome,
                MembrosCount = group.Membros.Count
            })
            .ToListAsync(cancellationToken);

        return new AgendaNotificationRecipientOptionsDto
        {
            TotalUsers = totalUsers, Page = request.Page, PageSize = request.PageSize,
            CanNotifyAllAllowedRecipients = true,
            AllRecipientsLabel = currentUser.IsEquipe
                ? "Todos os membros ativos desta equipe"
                : currentUser.IsMedico
                    ? "Administradores, superadministradores e controllers da clínica"
                    : "Todos os usuários ativos da clínica, exceto pacientes e você",
            Users = users,
            Groups = groups
        };
    }
}
