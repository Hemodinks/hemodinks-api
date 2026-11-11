namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Chave do anel de Data Protection usado na cifra do CPF (#227).
/// O XML fica cifrado pelo certificado configurado em CpfProtection; o banco sozinho nao decifra o CPF.
/// Tabela de plataforma (sem ClinicaId), acessada apenas por <see cref="Security.SqlDataProtectionXmlRepository"/>.
/// </summary>
public sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = null!;

    public DateTime DataCriacao { get; set; }
}
