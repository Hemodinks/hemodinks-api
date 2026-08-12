namespace HemodinksAPI.Domain.Models;

public static class EquipeModosIdentificacao
{
    public const string Nenhuma = "Nenhuma";
    public const string Selecao = "Selecao";
    public const string Pin = "Pin";

    public static readonly IReadOnlySet<string> Todos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Nenhuma,
        Selecao,
        Pin
    };
}

public sealed class Equipe : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public Clinica Clinica { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public int UsuarioLoginId { get; set; }
    public User UsuarioLogin { get; set; } = null!;
    public string ModoIdentificacao { get; set; } = EquipeModosIdentificacao.Pin;
    public bool Ativa { get; set; } = true;
    public int VersaoSessao { get; set; } = 1;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public ICollection<EquipeMembro> Membros { get; set; } = new List<EquipeMembro>();
    public ICollection<EquipeOperador> Operadores { get; set; } = new List<EquipeOperador>();
}

public sealed class EquipeMembro : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public Clinica Clinica { get; set; } = null!;
    public int EquipeId { get; set; }
    public Equipe Equipe { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}

public sealed class EquipeOperador : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public Clinica Clinica { get; set; } = null!;
    public int EquipeId { get; set; }
    public Equipe Equipe { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string? PinHash { get; set; }
    public bool PrecisaTrocarPin { get; set; }
    public int TentativasFalhas { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    public int VersaoSessao { get; set; } = 1;
    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
    public DateTime? DataUltimaTroca { get; set; }
    public DateTime? DataAtualizacao { get; set; }
}

public sealed class EquipeLoginDesafio : IClinicaOwnedEntity
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public Clinica Clinica { get; set; } = null!;
    public int EquipeId { get; set; }
    public Equipe Equipe { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime ExpiraEm { get; set; }
    public DateTime? UtilizadoEm { get; set; }
    public string? RequestIp { get; set; }
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}
