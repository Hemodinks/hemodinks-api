using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto isolado para operacoes explicitamente globais. Ele nao compartilha
/// o estado tenant mutavel do contexto usado pela requisicao.
/// </summary>
public sealed class PlatformDbContext : AppDbContext
{
    public PlatformDbContext(DbContextOptions<AppDbContext> options, ICpfProtector? cpfProtector = null)
        : base(options, ClinicaContextFactory.CreatePlatform(), cpfProtector)
    {
    }
}
