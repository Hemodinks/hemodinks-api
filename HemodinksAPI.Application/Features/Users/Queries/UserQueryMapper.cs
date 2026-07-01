using System.Linq.Expressions;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Users.Queries;

internal static class UserQueryMapper
{
    public static Expression<Func<User, UserDto>> ToListItemProjection()
    {
        return u => new UserDto
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
        };
    }

    public static Expression<Func<User, UserDto>> ToDetailsProjection()
    {
        return u => new UserDto
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
        };
    }

    public static UserArquivoDto ToArquivoDto(UserArquivo arquivo)
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

    public static Expression<Func<UserArquivo, UserArquivoDto>> ToArquivoProjection()
    {
        return arquivo => new UserArquivoDto
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
