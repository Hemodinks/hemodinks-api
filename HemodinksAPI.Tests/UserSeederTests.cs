using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Seeders;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;

namespace HemodinksAPI.Tests;

public class UserSeederTests
{
    [Fact]
    public void GenerateUsers_ReturnsExpectedInitialUsers()
    {
        var hasher = new PasswordHasher();
        var seeder = new UserSeeder(hasher);

        var users = seeder.GenerateUsers();

        Assert.Equal(20, users.Count);
        Assert.Equal(20, users.Select(user => user.Email).Distinct().Count());
        Assert.Equal(20, users.Select(user => user.Cpf).Distinct().Count());
        Assert.All(users, user =>
        {
            Assert.True(user.Ativo);
            Assert.True(user.PrecisaTrocarSenha);
            Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, user.Senha));
        });

        Assert.Contains(users, user =>
            user.Nome == "George Marcone Morais dos Santos" &&
            user.Email == "gmarcone@gmail.com" &&
            user.Telefone == "+5581997236704" &&
            user.DataNascimento == new DateTime(1982, 2, 25) &&
            user.PerfilId == Perfil.AdministradorId);

        Assert.Equal(9, users.Count(user => user.PerfilId == Perfil.MedicosId));
        Assert.Equal(10, users.Count(user => user.PerfilId == Perfil.PacientesId));
    }
}
