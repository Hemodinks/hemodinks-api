using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class PacienteCommandHandlerTests
{
    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        public Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
        {
            return Task.FromResult(fotoPerfil);
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

    private static ICbhpmCache CreateCbhpmCache(AppDbContext context)
    {
        return new CbhpmCache(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CbhpmCache>.Instance);
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
}
