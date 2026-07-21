using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace HemodinksAPI.Tests;

public class PrivateFileDownloadQueryHandlerTests
{
    [Fact]
    public async Task DownloadPacienteArquivo_WhenPatientIsInScope_ReturnsPrivateContent()
    {
        await using var context = TestDbContextFactory.Create();
        var paciente = CreatePatient("Paciente Download");
        var arquivo = CreatePatientFile(paciente, "laudo.pdf");
        context.PacienteArquivos.Add(arquivo);
        await context.SaveChangesAsync();
        var storage = new DownloadPatientFileStorage();
        var handler = new DownloadPacienteArquivoQueryHandler(context, storage);

        using var result = await handler.Handle(new DownloadPacienteArquivoQuery(
            paciente.Id,
            arquivo.Id,
            CurrentUserId: 99,
            CurrentPerfilId: Perfil.AdministradorId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("laudo.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(1, storage.GetCalls);
    }

    [Fact]
    public async Task DownloadPacienteArquivo_WhenDoctorCannotSeePatient_ReturnsNotFoundWithoutReadingBlob()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = CreateUser("Medico Sem Acesso", Perfil.MedicosId);
        var paciente = CreatePatient("Paciente Fora Do Escopo");
        var arquivo = CreatePatientFile(paciente, "sigiloso.pdf");
        context.Users.Add(doctor);
        context.PacienteArquivos.Add(arquivo);
        await context.SaveChangesAsync();
        var storage = new DownloadPatientFileStorage();
        var handler = new DownloadPacienteArquivoQueryHandler(context, storage);

        var result = await handler.Handle(new DownloadPacienteArquivoQuery(
            paciente.Id,
            arquivo.Id,
            doctor.Id,
            Perfil.MedicosId), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, storage.GetCalls);
    }

    [Fact]
    public async Task DownloadUserArquivo_WhenDoctorRequestsAnotherUser_IsRejectedBeforeReadingBlob()
    {
        await using var context = TestDbContextFactory.Create();
        var owner = CreateUser("Medico Proprietario", Perfil.MedicosId);
        var requester = CreateUser("Medico Solicitante", Perfil.MedicosId);
        var arquivo = new UserArquivo
        {
            User = owner,
            NomeOriginal = "crm.pdf",
            ContentType = "application/pdf",
            TamanhoBytes = 8,
            Url = "https://storage.example/crm.pdf"
        };
        context.Users.Add(requester);
        context.UserArquivos.Add(arquivo);
        await context.SaveChangesAsync();
        var storage = new DownloadPatientFileStorage();
        var handler = new DownloadUserArquivoQueryHandler(context, storage);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new DownloadUserArquivoQuery(
                owner.Id,
                arquivo.Id,
                new CurrentUserContext(requester.Id, Perfil.MedicosId, requester.Nome)),
            CancellationToken.None));

        Assert.Equal(0, storage.GetCalls);
    }

    private static Paciente CreatePatient(string name)
    {
        var user = CreateUser(name, Perfil.PacientesId);
        return new Paciente { User = user, NomePaciente = name };
    }

    private static User CreateUser(string name, int profileId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            Nome = name,
            Email = $"{suffix}@hemodinks.test",
            Telefone = "+5581999999999",
            Senha = "hash",
            PerfilId = profileId,
            Ativo = true
        };
    }

    private static PacienteArquivo CreatePatientFile(Paciente paciente, string name)
    {
        return new PacienteArquivo
        {
            Paciente = paciente,
            NomeOriginal = name,
            ContentType = "application/pdf",
            TamanhoBytes = 8,
            Url = $"https://storage.example/{name}"
        };
    }

    private sealed class DownloadPatientFileStorage : IPatientFileStorage
    {
        public int GetCalls { get; private set; }

        public Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult<StoredPatientFileContent?>(
                new StoredPatientFileContent(new MemoryStream("conteudo"u8.ToArray())));
        }

        public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
