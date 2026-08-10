using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

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
