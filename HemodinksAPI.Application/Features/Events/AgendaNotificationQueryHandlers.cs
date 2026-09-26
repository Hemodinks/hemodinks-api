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
        var currentUser = request.CurrentUser;
        if (currentUser.IsPaciente)
        {
            throw new UnauthorizedAccessException();
        }

        var usersQuery = EventRecipientScope.AllowedUsers(_context, currentUser);
        var groupsQuery = EventRecipientScope.AllowedGroups(_context, currentUser);

        var users = await usersQuery
            .OrderBy(user => user.Nome)
            .ThenBy(user => user.Id)
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
            CanNotifyAllAllowedRecipients = true,
            AllRecipientsLabel = currentUser.IsEquipe
                ? "Todos os membros ativos desta equipe"
                : currentUser.IsMedico
                    ? "Todos os administradores, controllers e medicos dos meus grupos"
                    : "Todos os usuarios ativos, exceto pacientes",
            Users = users,
            Groups = groups
        };
    }
}
