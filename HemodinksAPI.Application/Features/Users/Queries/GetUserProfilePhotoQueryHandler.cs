using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class GetUserProfilePhotoQueryHandler : IRequestHandler<GetUserProfilePhotoQuery, UserProfilePhotoDto?>
{
    private readonly IUserFeatureDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<GetUserProfilePhotoQueryHandler> _logger;

    public GetUserProfilePhotoQueryHandler(
        IUserFeatureDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<GetUserProfilePhotoQueryHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _logger = logger;
    }

    public async Task<UserProfilePhotoDto?> Handle(GetUserProfilePhotoQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(item => item.Id == request.Id)
                .Select(item => new
                {
                    item.Id,
                    item.FotoPerfil
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                return null;
            }

            await UserQueryAccess.EnsureCanAccessProfilePhotoAsync(_context, request.CurrentUser, request.Id, cancellationToken);

            var photo = await _profilePhotoStorage.GetAsync(user.FotoPerfil, cancellationToken);
            return photo == null
                ? null
                : new UserProfilePhotoDto
                {
                    Content = photo.Content,
                    ContentType = photo.ContentType
                };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar foto de perfil do usuario: {UserId}", request.Id);
            throw;
        }
    }
}
