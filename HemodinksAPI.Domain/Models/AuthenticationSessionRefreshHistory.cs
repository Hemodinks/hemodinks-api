namespace HemodinksAPI.Domain.Models;

// #389: familia de refresh tokens da sessao. Cada linha e o hash (SHA-256, nunca o token) de um token ja
// rotacionado; qualquer um que volte fora da janela de concorrencia revoga a sessao.
public class AuthenticationSessionRefreshHistory
{
    public long Id { get; set; }

    public Guid SessionId { get; set; }

    public AuthenticationSession Session { get; set; } = null!;

    public string Hash { get; set; } = null!;

    public DateTime RotatedAt { get; set; }
}
