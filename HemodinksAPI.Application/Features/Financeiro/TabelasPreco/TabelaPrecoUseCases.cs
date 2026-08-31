using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Financeiro;

public sealed class SalvarConvenioProcedimentoPrecoCommandHandler(IFinanceFeatureDbContext db, IClinicaContext tenant)
    : IRequestHandler<SalvarConvenioProcedimentoPrecoCommand, ConvenioProcedimentoPrecoDto>
{
    public async Task<ConvenioProcedimentoPrecoDto> Handle(SalvarConvenioProcedimentoPrecoCommand request, CancellationToken ct)
    {
        if (request.ConvenioId <= 0 || string.IsNullOrWhiteSpace(request.CbhpmCodigo))
        {
            throw new InvalidOperationException("Convenio e codigo CBHPM sao obrigatorios.");
        }

        if (request.ValorNegociado < 0 || request.PercentualPrincipal < 0 || request.PercentualAuxiliar1 < 0 || request.PercentualAuxiliar2 < 0
            || request.VigenciaFinal < request.VigenciaInicio)
            throw new InvalidOperationException("Valores ou vigencia do preco sao invalidos.");
        var code = request.CbhpmCodigo.Trim();
        var overlaps = await db.ConvenioProcedimentoPrecos.AnyAsync(x => x.Id != request.Id
            && x.ConvenioId == request.ConvenioId && x.CbhpmCodigo == code && x.Ativo && request.Ativo
            && x.VigenciaInicio <= (request.VigenciaFinal ?? DateTime.MaxValue)
            && (x.VigenciaFinal == null || x.VigenciaFinal >= request.VigenciaInicio), ct);
        if (overlaps) throw new InvalidOperationException("Ja existe preco ativo com vigencia sobreposta para o convenio e procedimento.");
        var item = request.Id.HasValue
            ? await db.ConvenioProcedimentoPrecos.SingleOrDefaultAsync(x => x.Id == request.Id, ct)
                ?? throw new KeyNotFoundException("Preco nao encontrado.")
            : new ConvenioProcedimentoPreco { ClinicaId = tenant.GetRequiredClinicaId() };
        item.ConvenioId = request.ConvenioId; item.CbhpmCodigo = code; item.ValorNegociado = request.ValorNegociado;
        item.PercentualPrincipal = request.PercentualPrincipal; item.PercentualAuxiliar1 = request.PercentualAuxiliar1;
        item.PercentualAuxiliar2 = request.PercentualAuxiliar2; item.VigenciaInicio = request.VigenciaInicio;
        item.VigenciaFinal = request.VigenciaFinal; item.Ativo = request.Ativo; item.DataAtualizacao = DateTime.UtcNow;
        if (!request.Id.HasValue) db.ConvenioProcedimentoPrecos.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    internal static ConvenioProcedimentoPrecoDto ToDto(ConvenioProcedimentoPreco x) => new(x.Id, x.ConvenioId,
        x.CbhpmCodigo, x.ValorNegociado, x.PercentualPrincipal, x.PercentualAuxiliar1, x.PercentualAuxiliar2,
        x.VigenciaInicio, x.VigenciaFinal, x.Ativo);
}

public sealed class ListarConvenioProcedimentoPrecosQueryHandler(IFinanceFeatureDbContext db)
    : IRequestHandler<ListarConvenioProcedimentoPrecosQuery, List<ConvenioProcedimentoPrecoDto>>
{
    public async Task<List<ConvenioProcedimentoPrecoDto>> Handle(ListarConvenioProcedimentoPrecosQuery request, CancellationToken ct)
    {
        var query = db.ConvenioProcedimentoPrecos.AsNoTracking();
        if (request.ConvenioId.HasValue) query = query.Where(x => x.ConvenioId == request.ConvenioId);
        if (!string.IsNullOrWhiteSpace(request.CbhpmCodigo)) query = query.Where(x => x.CbhpmCodigo == request.CbhpmCodigo.Trim());
        return (await query.OrderBy(x => x.CbhpmCodigo).ThenByDescending(x => x.VigenciaInicio).ToListAsync(ct))
            .Select(SalvarConvenioProcedimentoPrecoCommandHandler.ToDto).ToList();
    }
}


