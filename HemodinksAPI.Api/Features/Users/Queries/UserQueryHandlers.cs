using HemodinksAPI.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Users.Queries;

/// <summary>
/// Handler para buscar todos os usuários
/// </summary>
public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, List<UserDto>>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;

    public GetAllUsersQueryHandler(AppDbContext context, ILogger<GetAllUsersQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<UserDto>> Handle(GetAllUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Buscando todos os usuários");

            var users = await _context.Users
                .AsNoTracking()
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    DataCadastro = u.DataCadastro,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha
                })
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Encontrados {Count} usuários", users.Count);

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuários");
            throw;
        }
    }
}

/// <summary>
/// Handler para buscar usuário por ID
/// </summary>
public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetUserByIdQueryHandler> _logger;

    public GetUserByIdQueryHandler(AppDbContext context, ILogger<GetUserByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Buscando usuário por ID: {UserId}", request.Id);

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == request.Id)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    DataCadastro = u.DataCadastro,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado. ID: {UserId}", request.Id);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuário por ID: {UserId}", request.Id);
            throw;
        }
    }
}

/// <summary>
/// Handler para buscar usuário por email
/// </summary>
public class GetUserByEmailQueryHandler : IRequestHandler<GetUserByEmailQuery, UserDto?>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetUserByEmailQueryHandler> _logger;

    public GetUserByEmailQueryHandler(AppDbContext context, ILogger<GetUserByEmailQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserDto?> Handle(GetUserByEmailQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Buscando usuário por email: {Email}", request.Email);

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == request.Email)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    DataCadastro = u.DataCadastro,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuário não encontrado. Email: {Email}", request.Email);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuário por email: {Email}", request.Email);
            throw;
        }
    }
}
