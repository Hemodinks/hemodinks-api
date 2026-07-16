using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Fact]
    public async Task UploadUserArquivo_WhenDoctorUploadsForAnotherUser_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new UploadUserArquivoCommandHandler(
            context,
            new FakePatientFileStorage(),
            NullLogger<UploadUserArquivoCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UploadUserArquivoCommand
        {
            UserId = 10,
            CurrentUser = new CurrentUserContext(99, Perfil.MedicosId, "Outro Medico"),
            File = default!
        }, CancellationToken.None));
    }

}
