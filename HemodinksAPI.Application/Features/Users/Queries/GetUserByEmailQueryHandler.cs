using HemodinksAPI.Application.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Queries;

public class GetUserByEmailQueryHandler : IRequestHandler<GetUserByEmailQuery, UserDto?>
{
    private readonly IUserDbContext _context;
    private readonly ILogger<GetUserByEmailQueryHandler> _logger;

    public GetUserByEmailQueryHandler(IUserDbContext context, ILogger<GetUserByEmailQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(GetUserByEmailQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var maskedEmail = HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(request.Email);
            _logger.LogInformation("Buscando usuario por email: {MaskedEmail}", maskedEmail);

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == request.Email)
                .Select(UserQueryMapper.ToListItemProjection())
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuario nao encontrado. Email: {MaskedEmail}", maskedEmail);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuario por email");
            throw;
        }
    }
}
