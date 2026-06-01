using HemodinksAPI.Api.Features.Pacientes.Commands;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Storage;
using HemodinksAPI.Api.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class PacienteCommandHandlerTests
{
    [Fact]
    public async Task CreatePaciente_CreatesLinkedUserWithPacienteProfile()
    {
        await using var context = TestDbContextFactory.Create();
        var hasher = new PasswordHasher();
        var handler = new CreatePacienteCommandHandler(
            context,
            hasher,
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Novo",
            Email = "paciente.novo@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            Hospital = "Hospital Hemodinks",
            Medico = "Dra. Ana",
            Convenio = "Particular",
            Procedimento = "Consulta",
            Autorizacao = "AUT-123",
            Pagamento = "Pix",
            RepasseGlosa = "Sem glosa",
            StatusPago = true
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync();
        var storedPaciente = await context.Pacientes.SingleAsync();

        Assert.Equal(storedUser.Id, storedPaciente.UserId);
        Assert.Equal(Perfil.PacientesId, storedUser.PerfilId);
        Assert.Equal("Paciente Novo", storedUser.Nome);
        Assert.Equal("52998224725", storedUser.Cpf);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.Equal("Hospital Hemodinks", storedPaciente.Hospital);
        Assert.True(storedPaciente.StatusPago);
        Assert.Equal(storedPaciente.Id, response.Id);
        Assert.Equal(storedUser.Id, response.UserId);
    }

    [Fact]
    public async Task UploadPacienteArquivo_WhenPacienteExists_StoresMetadata()
    {
        await using var context = TestDbContextFactory.Create();
        var user = new User
        {
            Nome = "Paciente Upload",
            Email = "paciente.upload@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UploadPacienteArquivoCommandHandler(
            context,
            new FakePatientFileStorage(),
            NullLogger<UploadPacienteArquivoCommandHandler>.Instance);

        var file = new FormFile(new MemoryStream("conteudo"u8.ToArray()), 0, 8, "file", "laudo.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var response = await handler.Handle(new UploadPacienteArquivoCommand
        {
            PacienteId = paciente.Id,
            File = file
        }, CancellationToken.None);

        var storedArquivo = await context.PacienteArquivos.SingleAsync();
        Assert.Equal(paciente.Id, storedArquivo.PacienteId);
        Assert.Equal("laudo.pdf", storedArquivo.NomeOriginal);
        Assert.Equal("https://storage.example/laudo.pdf", storedArquivo.Url);
        Assert.Equal(storedArquivo.Id, response.Id);
    }

    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        public Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
        {
            return Task.FromResult(fotoPerfil);
        }

        public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePatientFileStorage : IPatientFileStorage
    {
        public Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult(new StoredPatientFile(
                file.FileName,
                file.ContentType,
                file.Length,
                $"https://storage.example/{file.FileName}"));
        }

        public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
