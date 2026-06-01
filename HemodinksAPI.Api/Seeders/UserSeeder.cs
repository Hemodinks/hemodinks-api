using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Utils;

namespace HemodinksAPI.Api.Seeders;

/// <summary>
/// Seeder para popular dados iniciais de usuários
/// </summary>
public class UserSeeder
{
    private readonly IPasswordHasher _passwordHasher;

    public UserSeeder(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    /// <summary>
    /// Popula o banco com 50 usuários
    /// </summary>
    public List<User> GenerateUsers()
    {
        var users = new List<User>();

        // Adicionar o usuário específico (George Marcone)
        users.Add(new User
        {
            Nome = "George Marcone Morais dos Santos",
            Email = "gmarcone@gmail.com",
            Telefone = "+5581997236704",
            Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1982, 2, 25),
            DataCadastro = DateTime.UtcNow,
            Ativo = true,
            PrecisaTrocarSenha = true,
            PerfilId = Perfil.AdministradorId
        });

        // Adicionar 49 usuários adicionais
        var nomes = new[]
        {
            "Maria Silva", "João Santos", "Ana Costa", "Pedro Oliveira", "Carla Souza",
            "Lucas Martins", "Juliana Ferreira", "Rafael Alves", "Beatriz Rocha", "Felipe Gomes",
            "Isabela Dias", "Gustavo Pereira", "Vanessa Ribeiro", "Diego Almeida", "Patricia Mendes",
            "Ricardo Cardoso", "Camila Teixeira", "Bruno Nascimento", "Amanda Barbosa", "Thiago Machado",
            "Fernanda Moreira", "Rodrigo Cavalcanti", "Carolina Monteiro", "Marcelo Duarte", "Larissa Pinto",
            "Antonio Castro", "Mariana Lopes", "Eduardo Correia", "Gabriela Fonseca", "Carlos Maia",
            "Leticia Saraiva", "Sergio Perez", "Fernanda Siqueira", "Paulo Vidal", "Adriana Freitas",
            "Matheus Brito", "Joana Benavides", "Fabio Rangel", "Sofia Delgado", "Victor Santos",
            "Renata Campos", "Alexandre Barbosa", "Luisa Carvalho", "Leandro Paz", "Monica Olivares",
            "Cesar Vinuesa", "Victoria Rojas", "Jonathan Deleon", "Viviana Gutierrez"
        };

        var emails = new[]
        {
            "maria.silva@email.com", "joao.santos@email.com", "ana.costa@email.com", "pedro.oliveira@email.com",
            "carla.souza@email.com", "lucas.martins@email.com", "juliana.ferreira@email.com", "rafael.alves@email.com",
            "beatriz.rocha@email.com", "felipe.gomes@email.com", "isabela.dias@email.com", "gustavo.pereira@email.com",
            "vanessa.ribeiro@email.com", "diego.almeida@email.com", "patricia.mendes@email.com", "ricardo.cardoso@email.com",
            "camila.teixeira@email.com", "bruno.nascimento@email.com", "amanda.barbosa@email.com", "thiago.machado@email.com",
            "fernanda.moreira@email.com", "rodrigo.cavalcanti@email.com", "carolina.monteiro@email.com", "marcelo.duarte@email.com",
            "larissa.pinto@email.com", "antonio.castro@email.com", "mariana.lopes@email.com", "eduardo.correia@email.com",
            "gabriela.fonseca@email.com", "carlos.maia@email.com", "leticia.saraiva@email.com", "sergio.perez@email.com",
            "fernanda.siqueira@email.com", "paulo.vidal@email.com", "adriana.freitas@email.com", "matheus.brito@email.com",
            "joana.benavides@email.com", "fabio.rangel@email.com", "sofia.delgado@email.com", "victor.santos@email.com",
            "renata.campos@email.com", "alexandre.barbosa@email.com", "luisa.carvalho@email.com", "leandro.paz@email.com",
            "monica.olivares@email.com", "cesar.vinuesa@email.com", "victoria.rojas@email.com", "jonathan.deleon@email.com",
            "viviana.gutierrez@email.com"
        };

        var telefones = new[]
        {
            "+5511987654321", "+5585912345678", "+5521988776655", "+5531999887766", "+5541987654321",
            "+5548912345678", "+5562999887766", "+5567912345678", "+5575987654321", "+5584912345678",
            "+5585988776655", "+5586999887766", "+5587912345678", "+5588987654321", "+5589912345678",
            "+5511912345678", "+5512987654321", "+5513988776655", "+5514999887766", "+5515912345678",
            "+5516987654321", "+5517988776655", "+5518999887766", "+5519912345678", "+5520987654321",
            "+5521912345678", "+5522988776655", "+5523999887766", "+5524912345678", "+5525987654321",
            "+5526988776655", "+5527999887766", "+5528912345678", "+5529987654321", "+5530988776655",
            "+5532999887766", "+5533912345678", "+5534987654321", "+5535988776655", "+5536999887766",
            "+5537912345678", "+5538987654321", "+5539988776655", "+5540999887766", "+5542912345678",
            "+5543987654321", "+5544988776655", "+5545999887766", "+5546912345678"
        };

        var random = new Random();

        for (int i = 0; i < 49; i++)
        {
            var dataNascimento = new DateTime(
                random.Next(1960, 2005),
                random.Next(1, 13),
                random.Next(1, 29)
            );

            users.Add(new User
            {
                Nome = nomes[i],
                Email = emails[i],
                Telefone = telefones[i],
                Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value),
                DataNascimento = dataNascimento,
                DataCadastro = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                Ativo = true,
                PrecisaTrocarSenha = true,
                PerfilId = Perfil.MedicosId
            });
        }

        return users;
    }
}
