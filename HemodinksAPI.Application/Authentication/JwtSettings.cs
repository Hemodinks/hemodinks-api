namespace HemodinksAPI.Application.Authentication;

/// <summary>
/// Configurações de JWT
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Chave secreta para assinar os tokens
    /// </summary>
    public string SecretKey { get; set; } = null!;

    /// <summary>
    /// Emissor do token
    /// </summary>
    public string Issuer { get; set; } = "HemodinksAPI";

    /// <summary>
    /// Audiência do token
    /// </summary>
    public string Audience { get; set; } = "HemodinksAPI";

    /// <summary>
    /// Tempo de expiração em minutos
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;
}
