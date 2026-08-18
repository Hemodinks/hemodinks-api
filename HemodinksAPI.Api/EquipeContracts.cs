namespace HemodinksAPI.Api;

public sealed record CriarEquipeRequest(string Nome, string Email, string Senha, string? Telefone, string? ModoIdentificacao);
public sealed record AtualizarEquipeRequest(string? Nome, string? ModoIdentificacao, bool? Ativa);
public sealed record AssociarEquipeMembroRequest(int UserId, bool GerarPin);
public sealed record AlterarBloqueioOperadorRequest(bool Bloqueado);
public sealed record IdentificarEquipeRequest(string Token, int OperadorId, string? Pin);
public sealed record TrocarEquipePinRequest(string PinAtual, string NovoPin);
