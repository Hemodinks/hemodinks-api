using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    private static User CreateUser(
        string email,
        string passwordHash,
        int id = 0,
        bool precisaTrocarSenha = true,
        string? fotoPerfil = null,
        int perfilId = Perfil.MedicosId)
    {
        return new User
        {
            Id = id,
            Nome = "Usuario Teste",
            Email = email,
            Telefone = "+5511999999999",
            Cpf = "52998224725",
            Crm = "54321",
            CrmUf = "SP",
            Senha = passwordHash,
            DataCadastro = DateTime.UtcNow,
            DataNascimento = new DateTime(1990, 1, 1),
            Ativo = true,
            PrecisaTrocarSenha = precisaTrocarSenha,
            FotoPerfil = fotoPerfil,
            PerfilId = perfilId
        };
    }


    private static LicencaService CreateLicencaService(HemodinksAPI.Infrastructure.Data.AppDbContext context)
    {
        return new LicencaService(context, Options.Create(new LicencaOptions()));
    }

    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        private int _saveCount;

        public Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fotoPerfil))
            {
                return Task.FromResult<string?>(null);
            }

            if (fotoPerfil.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                _saveCount++;
                return Task.FromResult<string?>($"https://storage.example/{_saveCount}.png");
            }

            return Task.FromResult<string?>(fotoPerfil);
        }

        public Task<ProfilePhotoFile?> GetAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.FromResult<ProfilePhotoFile?>(null);
        }

        public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        private readonly string _token;

        public StubJwtTokenService(string token)
        {
            _token = token;
        }

        public string GenerateToken(User user)
        {
            return _token;
        }

        public string GenerateToken(User user, Guid? sessionId = null)
        {
            return _token;
        }

        public string GenerateToken(
            UsuarioGlobal usuarioGlobal,
            UsuarioClinica usuarioClinica,
            User user,
            Guid? sessionId = null)
        {
            return _token;
        }

        public string GenerateToken(
            UsuarioGlobal usuarioGlobal,
            UsuarioClinica usuarioClinica,
            User user,
            Equipe? equipe = null,
            EquipeOperador? operador = null,
            bool identificacaoConfiavel = false)
        {
            return _token;
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

        public Task<StoredPatientFileContent?> GetAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult<StoredPatientFileContent?>(
                new StoredPatientFileContent(new MemoryStream("conteudo"u8.ToArray())));
        }
    }

    private sealed class FakePasswordResetNotificationSender : IPasswordResetNotificationSender
    {
        public List<PasswordResetNotification> Notifications { get; } = new();

        public PasswordResetNotificationDispatchStatus DispatchStatus { get; set; } = PasswordResetNotificationDispatchStatus.Sent;

        public Exception? ExceptionToThrow { get; set; }

        public Task<PasswordResetNotificationDispatchStatus> SendAsync(
            PasswordResetNotification notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(DispatchStatus);
        }
    }
}
