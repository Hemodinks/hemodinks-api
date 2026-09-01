using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
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
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
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
            File = new UploadedFile(file.FileName, file.ContentType, file.Length, file.OpenReadStream),
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedArquivo = await context.PacienteArquivos.SingleAsync();
        Assert.Equal(paciente.Id, storedArquivo.PacienteId);
        Assert.Equal("laudo.pdf", storedArquivo.NomeOriginal);
        Assert.Equal("https://storage.example/laudo.pdf", storedArquivo.Url);
        Assert.Equal(storedArquivo.Id, response.Id);
    }

    [Fact]
    public async Task UploadPacienteArquivo_WhenLoggedDoctorIsRelated_StoresMetadata()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.upload@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Upload Bloqueado",
            Email = "paciente.upload.bloqueado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            HospitalId = 2,
            Hospital = "Hospital Controller",
            MedicoUser = doctor,
            Medico = doctor.Nome
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
            File = new UploadedFile(file.FileName, file.ContentType, file.Length, file.OpenReadStream),
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        var storedArquivo = await context.PacienteArquivos.SingleAsync();
        Assert.Equal(paciente.Id, storedArquivo.PacienteId);
        Assert.Equal("laudo.pdf", storedArquivo.NomeOriginal);
        Assert.Equal("https://storage.example/laudo.pdf", storedArquivo.Url);
        Assert.Equal(storedArquivo.Id, response.Id);
    }

    [Fact]
    public async Task DeletePacienteArquivo_WhenLoggedDoctorIsRelated_DeletesArquivo()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.delete@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Delete",
            Email = "paciente.delete@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(TestPasswords.Valid),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctor.Nome
        };
        var arquivo = new PacienteArquivo
        {
            Paciente = paciente,
            NomeOriginal = "laudo.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 8,
            Url = "https://storage.example/laudo.pdf",
            DataUpload = DateTime.UtcNow
        };
        context.PacienteArquivos.Add(arquivo);
        await context.SaveChangesAsync();

        var handler = new DeletePacienteArquivoCommandHandler(
            context,
            new FakePatientFileStorage(),
            NullLogger<DeletePacienteArquivoCommandHandler>.Instance);

        await handler.Handle(new DeletePacienteArquivoCommand
        {
            PacienteId = paciente.Id,
            ArquivoId = arquivo.Id,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None);

        Assert.Empty(await context.PacienteArquivos.ToListAsync());
    }

}
