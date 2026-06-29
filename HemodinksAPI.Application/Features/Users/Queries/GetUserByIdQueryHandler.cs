using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetUserByIdQueryHandler> _logger;

    public GetUserByIdQueryHandler(IAppDbContext context, ILogger<GetUserByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Buscando usuario por ID: {UserId}", request.Id);

            UserQueryAccess.EnsureCanAccessUser(request.CurrentUser, request.Id);

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == request.Id)
                .Select(UserQueryMapper.ToDetailsProjection())
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuario nao encontrado. ID: {UserId}", request.Id);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuario por ID: {UserId}", request.Id);
            throw;
        }
    }
}
