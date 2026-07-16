using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;

namespace HemodinksAPI.Application.Features.GruposMedicos.Commands;

public class CreateGrupoMedicoCommandHandler : IRequestHandler<CreateGrupoMedicoCommand, GrupoMedicoDto>
{
    private readonly IAppDbContext _context;
    private readonly IClinicaContext _clinicaContext;

    public CreateGrupoMedicoCommandHandler(IAppDbContext context, IClinicaContext clinicaContext)
    {
        _context = context;
        _clinicaContext = clinicaContext;
    }

    public async Task<GrupoMedicoDto> Handle(CreateGrupoMedicoCommand request, CancellationToken cancellationToken)
    {
        var clinicaId = _clinicaContext.GetRequiredClinicaId();
        var nome = request.Nome.Trim();
        var memberIds = GrupoMedicoRules.NormalizeMemberIds(request.MedicoUserIds);

        await GrupoMedicoRules.ValidateAsync(_context, null, nome, memberIds, cancellationToken);

        var now = DateTime.UtcNow;
        var group = new GrupoMedico
        {
            ClinicaId = clinicaId,
            Nome = nome,
            Ativo = request.Ativo,
            DataCadastro = now,
            Membros = memberIds
                .Select(userId => new GrupoMedicoUsuario
                {
                    ClinicaId = clinicaId,
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
