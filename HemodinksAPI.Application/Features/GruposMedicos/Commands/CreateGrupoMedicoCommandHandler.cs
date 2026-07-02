using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class CreateGrupoMedicoCommandHandler : IRequestHandler<CreateGrupoMedicoCommand, GrupoMedicoDto>
{
    private readonly IAppDbContext _context;

    public CreateGrupoMedicoCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<GrupoMedicoDto> Handle(CreateGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var nome = request.Nome.Trim();
        var memberIds = GrupoMedicoRules.NormalizeMemberIds(request.MedicoUserIds);

        await GrupoMedicoRules.ValidateAsync(_context, null, nome, memberIds, cancellationToken);

        var now = DateTime.UtcNow;
        var group = new GrupoMedico
        {
            Nome = nome,
            Ativo = request.Ativo,
            DataCadastro = now,
            Membros = memberIds
                .Select(userId => new GrupoMedicoUsuario
                {
                    UserId = userId,
                    DataCadastro = now
                })
                .ToList()
        };

        _context.GruposMedicos.Add(group);
        await _context.SaveChangesAsync(cancellationToken);

        return await GrupoMedicoRules.GetDtoAsync(_context, group.Id, cancellationToken);
    }
}
