namespace HemodinksAPI.Domain.Models;

public class Recebimento : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; } = Clinica.DefaultId;
    public Clinica Clinica { get; set; } = null!;
    public int ContaReceberId { get; set; }
    public ContaReceber ContaReceber { get; set; } = null!;
    public DateTime DataRecebimento { get; set; }
    public decimal ValorRecebido { get; set; }
    public FormaRecebimento FormaRecebimento { get; set; }
    public string? ReferenciaBancaria { get; set; }
    public string? DocumentoComprovante { get; set; }
    public string? Observacao { get; set; }
    public int UsuarioCadastroId { get; set; }
    public User UsuarioCadastro { get; set; } = null!;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public bool Estornado { get; set; }
    public DateTime? DataEstorno { get; set; }
    public int? UsuarioEstornoId { get; set; }
    public User? UsuarioEstorno { get; set; }
    public string? MotivoEstorno { get; set; }
}
