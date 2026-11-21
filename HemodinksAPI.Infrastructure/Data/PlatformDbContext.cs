using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto isolado para operacoes explicitamente globais. Ele nao compartilha
/// o estado tenant mutavel do contexto usado pela requisicao.
/// </summary>
/// <remarks>
/// <see cref="IPlatformSessionMonitorDbContext"/> e implementada so aqui (#294): registrar a interface
/// com o <see cref="AppDbContext"/> da requisicao nem compila, em vez de devolver dados de um tenant.
/// </remarks>
public sealed class PlatformDbContext : AppDbContext, IPlatformSessionMonitorDbContext
{
    public PlatformDbContext(DbContextOptions<AppDbContext> options, ICpfProtector? cpfProtector = null)
        : base(options, ClinicaContextFactory.CreatePlatform(), cpfProtector)
    {
    }
}
