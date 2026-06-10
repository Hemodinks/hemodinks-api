using MediatR;

namespace HemodinksAPI.Application.Features.Cbhpm.Commands;

public class ImportCbhpmGeralCommand : IRequest<CbhpmImportResultDto>
{
    public List<CbhpmImportItemDto> Items { get; set; } = [];
}

public class CbhpmImportItemDto
{
    public string Codigo { get; set; } = null!;
    public string Procedimento { get; set; } = null!;
    public string? Porte { get; set; }
    public decimal? CustoOperacional { get; set; }
    public decimal? ValorReferencia { get; set; }
    public string? Capitulo { get; set; }
    public string? Grupo { get; set; }
    public int? PaginaPdf { get; set; }
}

public class CbhpmImportResultDto
{
    public int TotalItems { get; set; }
    public int InsertedItems { get; set; }
    public int UpdatedItems { get; set; }
}
