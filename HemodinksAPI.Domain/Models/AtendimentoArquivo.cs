namespace HemodinksAPI.Domain.Models;

public class AtendimentoArquivo : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int AtendimentoCirurgicoId { get; set; }
    public AtendimentoCirurgico AtendimentoCirurgico { get; set; } = null!;
    public string NomeOriginal { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long TamanhoBytes { get; set; }
    public string Url { get; set; } = null!;
    public DateTime DataUpload { get; set; } = DateTime.UtcNow;
    // Exclusao logica: o arquivo some da aplicacao, mas linha e storage sao preservados (guarda do prontuario).
    public DateTime? ExcluidoEm { get; set; }
    public int? ExcluidoPorUserId { get; set; }
}
