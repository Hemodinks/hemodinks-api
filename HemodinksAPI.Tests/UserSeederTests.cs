using HemodinksAPI.Api.Seeders;
using HemodinksAPI.Api.Utils;

namespace HemodinksAPI.Tests;

public class UserSeederTests
{
    [Fact]
    public void GenerateUsers_ReturnsExpectedInitialUsers()
    {
        var hasher = new PasswordHasher();
        var seeder = new UserSeeder(hasher);

        var users = seeder.GenerateUsers();

        Assert.Equal(50, users.Count);
        Assert.Equal(50, users.Select(user => user.Email).Distinct().Count());
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
            user.DataNascimento == new DateTime(1982, 2, 25));
    }
}
