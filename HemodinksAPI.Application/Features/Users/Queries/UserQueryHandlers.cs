using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Common;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Users.Queries;

public class GetAllUsersQueryHandler : IRequestHandler<GetAllUsersQuery, PagedResult<UserDto>>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;

    public GetAllUsersQueryHandler(IAppDbContext context, ILogger<GetAllUsersQueryHandler> logger)
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
                    Crm = u.Crm,
                    CrmUf = u.CrmUf,
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

            if (request.CurrentUser != null
                && !request.CurrentUser.IsAdministrador
                && request.CurrentUser.Id != request.Id)
            {
                throw new UnauthorizedAccessException("Sem permissao para acessar usuario");
            }

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == request.Id)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Nome = u.Nome,
                    Email = u.Email,
                    Telefone = u.Telefone,
                    Cpf = u.Cpf,
                    Crm = u.Crm,
                    CrmUf = u.CrmUf,
                    FotoPerfil = u.FotoPerfil,
                    DataCadastro = u.DataCadastro,
                    DataAtualizacao = u.DataAtualizacao,
                    DataNascimento = u.DataNascimento,
                    Ativo = u.Ativo,
                    PrecisaTrocarSenha = u.PrecisaTrocarSenha,
                    PerfilId = u.PerfilId,
                    PerfilNome = u.Perfil.Nome,
                    ArquivosCount = u.Arquivos.Count,
                    Arquivos = u.Arquivos
                        .OrderByDescending(arquivo => arquivo.DataUpload)
                        .Select(arquivo => new UserArquivoDto
                        {
                            Id = arquivo.Id,
                            NomeOriginal = arquivo.NomeOriginal,
                            ContentType = arquivo.ContentType,
                            TamanhoBytes = arquivo.TamanhoBytes,
                            Url = arquivo.Url,
                            DataUpload = arquivo.DataUpload
                        })
                        .ToList()
                })
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

public class GetUserProfilePhotoQueryHandler : IRequestHandler<GetUserProfilePhotoQuery, UserProfilePhotoDto?>
{
    private readonly IAppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<GetUserProfilePhotoQueryHandler> _logger;

    public GetUserProfilePhotoQueryHandler(
        IAppDbContext context,
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

            if (!request.CurrentUser.IsAdministrador && request.CurrentUser.Id != request.Id)
            {
                var canAccessPatientPhoto = request.CurrentUser.IsMedico
                    && await _context.Pacientes
                        .AsNoTracking()
                        .AnyAsync(paciente =>
                            paciente.UserId == request.Id
                            && (paciente.MedicoUserId == request.CurrentUser.Id || paciente.Medico == request.CurrentUser.Nome),
                            cancellationToken);

                if (!canAccessPatientPhoto)
                {
                    throw new UnauthorizedAccessException("Sem permissao para acessar foto de perfil");
                }
            }

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

public class GetUserByEmailQueryHandler : IRequestHandler<GetUserByEmailQuery, UserDto?>
{
    private readonly IAppDbContext _context;
    private readonly ILogger<GetUserByEmailQueryHandler> _logger;

    public GetUserByEmailQueryHandler(IAppDbContext context, ILogger<GetUserByEmailQueryHandler> logger)
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
                    Crm = u.Crm,
                    CrmUf = u.CrmUf,
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
