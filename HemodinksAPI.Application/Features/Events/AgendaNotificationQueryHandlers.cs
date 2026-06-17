using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Events;

public sealed class GetAgendaNotificationRecipientOptionsQueryHandler
    : IRequestHandler<GetAgendaNotificationRecipientOptionsQuery, AgendaNotificationRecipientOptionsDto>
{
    private readonly IAppDbContext _context;

    public GetAgendaNotificationRecipientOptionsQueryHandler(IAppDbContext context)
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

        var isAdminOrController = currentUser.IsAdministrador || currentUser.IsController;

        var usersQuery = _context.Users
            .AsNoTracking()
            .Where(user => user.Ativo && user.Id != currentUser.Id);

        if (isAdminOrController)
        {
            usersQuery = usersQuery.Where(user => user.PerfilId != Perfil.PacientesId);
        }
        else if (currentUser.IsMedico)
        {
            usersQuery = usersQuery.Where(user =>
                user.PerfilId == Perfil.AdministradorId
                || user.PerfilId == Perfil.ControllerId);
        }
        else
        {
            throw new UnauthorizedAccessException();
        }

        var groupsQuery = _context.GruposMedicos
            .AsNoTracking()
            .Where(group => group.Ativo);

        if (currentUser.IsMedico)
        {
            groupsQuery = groupsQuery.Where(group => group.Membros.Any(member => member.UserId == currentUser.Id));
        }
        else if (!isAdminOrController)
        {
            groupsQuery = groupsQuery.Where(_ => false);
        }

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
            AllRecipientsLabel = currentUser.IsMedico
                ? "Todos os administradores, controllers e medicos dos meus grupos"
                : "Todos os usuarios ativos, exceto pacientes",
            Users = users,
            Groups = groups
        };
    }
}
