namespace HemodinksAPI.Domain.Models;

public class FaturamentoMedico
{
    public int Id { get; set; }

    public int PacienteId { get; set; }

    public Paciente Paciente { get; set; } = null!;

    public decimal? HonorariosCirurgiao { get; set; }

    public decimal? HonorariosAuxiliares { get; set; }

    public decimal? HonorariosAnestesista { get; set; }

    public bool AnestesistaFaturadoSeparado { get; set; }

    public string? Anestesista { get; set; }

    public string? CodigoTussCbhpmAmb { get; set; }

    public string? PorteCirurgicoAnestesico { get; set; }

    public string? GuiaAutorizacaoConvenio { get; set; }

    public string? GuiaInternacaoOuSadt { get; set; }

    public string? OpmeMateriaisEspeciais { get; set; }

    public string? TissXmlStatus { get; set; }

    public decimal? ValorGlosa { get; set; }

    public string? GlosaStatus { get; set; }

    public string? RecursoGlosa { get; set; }

    public bool ConferenciaPagamentoRealizada { get; set; }

    public decimal? RepasseMedico { get; set; }

    public string? RepasseMedicoObservacao { get; set; }

    public string? TipoFaturamentoParticular { get; set; }

    public string? ReciboNotaContrato { get; set; }

    public string? Observacoes { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }
}
