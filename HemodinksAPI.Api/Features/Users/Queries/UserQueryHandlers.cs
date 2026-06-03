using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Users.Queries;

public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, PagedResult<UserDto>>
{
    private readonly AppDbContext _context;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;

    public GetAllUsersQueryHandler(AppDbContext context, ILogger<GetAllUsersQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<UserDto>> Handle(GetAllUsersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);
            var search = request.Search?.Trim();
            var digits = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : new string(search.Where(char.IsDigit).ToArray());

            _logger.LogInformation("Buscando usuarios. Pagina: {Page}, Tamanho: {PageSize}", page, pageSize);

            var query = _context.Users.AsNoTracking();

            if (request.ProfileId.HasValue)
            {
                query = query.Where(u => u.PerfilId == request.ProfileId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(u =>
                    u.Nome.Contains(search)
                    || u.Email.Contains(search)
                    || u.Telefone.Contains(search)
                    || u.Perfil.Nome.Contains(search)
                    || (!string.IsNullOrEmpty(digits) && u.Cpf != null && u.Cpf.Contains(digits))
                    || (!string.IsNullOrEmpty(digits) && u.Telefone.Contains(digits)));
            }

            var totalItems = await query.CountAsync(cancellationToken);

            var users = await query
                .OrderByDescending(u => u.DataAtualizacao ?? u.DataCadastro)
                .ThenBy(u => u.Nome)
                .ThenByDescending(u => u.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    Cpf = u.Cpf,
                    FotoPerfil = u.FotoPerfil,
                    DataCadastro = u.DataCadastro,
                    DataAtualizacao = u.DataAtualizacao,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha,
                    PerfilId = u.PerfilId,
                    PerfilNome = u.Perfil.Nome,
                    ArquivosCount = u.Arquivos.Count
                })
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Encontrados {Count} usuarios na pagina de {Total} registros", users.Count, totalItems);

            return new PagedResult<UserDto>
            {
                Items = users,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuarios");
            throw;
        }
    }
}

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
            _logger.LogInformation("Buscando usuario por ID: {UserId}", request.Id);

            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Perfil)
                .Include(u => u.Arquivos)
                .AsSplitQuery()
                .Where(u => u.Id == request.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuario nao encontrado. ID: {UserId}", request.Id);
            }

            return user == null ? null : UserMapper.ToDto(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuario por ID: {UserId}", request.Id);
            throw;
        }
    }
}

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
            _logger.LogInformation("Buscando usuario por email: {Email}", request.Email);

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == request.Email)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    Cpf = u.Cpf,
                    FotoPerfil = u.FotoPerfil,
                    DataCadastro = u.DataCadastro,
                    DataAtualizacao = u.DataAtualizacao,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha,
                    PerfilId = u.PerfilId,
                    PerfilNome = u.Perfil.Nome,
                    ArquivosCount = u.Arquivos.Count
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                _logger.LogWarning("Usuario nao encontrado. Email: {Email}", request.Email);
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar usuario por email: {Email}", request.Email);
            throw;
        }
    }
}

internal static class UserMapper
{
    public static UserDto ToDto(Models.User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Nome = user.Nome,
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf,
            FotoPerfil = user.FotoPerfil,
            DataCadastro = user.DataCadastro,
            DataAtualizacao = user.DataAtualizacao,
            DataNascimento = user.DataNascimento,
            Ativo = user.Ativo,
            PrecisaTrocarSenha = user.PrecisaTrocarSenha,
            PerfilId = user.PerfilId,
            PerfilNome = user.Perfil.Nome,
            ArquivosCount = user.Arquivos.Count,
            Arquivos = user.Arquivos
                .OrderByDescending(arquivo => arquivo.DataUpload)
                .Select(ToArquivoDto)
                .ToList()
        };
    }

    public static UserArquivoDto ToArquivoDto(Models.UserArquivo arquivo)
    {
        return new UserArquivoDto
        {
            Id = arquivo.Id,
            NomeOriginal = arquivo.NomeOriginal,
            ContentType = arquivo.ContentType,
            TamanhoBytes = arquivo.TamanhoBytes,
            Url = arquivo.Url,
            DataUpload = arquivo.DataUpload
        };
    }
}
