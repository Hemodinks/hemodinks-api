using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto isolado para operacoes explicitamente globais. Ele nao compartilha
/// o estado tenant mutavel do contexto usado pela requisicao.
/// </summary>
/// <remarks>
/// As interfaces de escopo de plataforma sao implementadas so aqui (#294, #359): registra-las com o
/// <see cref="AppDbContext"/> da requisicao nem compila, em vez de devolver vazio ou dados de um tenant.
/// </remarks>
public sealed class PlatformDbContext : AppDbContext,
    IPlatformSessionMonitorDbContext,
    ISensitiveIdentityDbContext,
    ITemporaryAccessDbContext,
    IPlatformPasswordResetDbContext,
    IPlatformClinicDbContext,
    ISessionDbContext
{
    public PlatformDbContext(DbContextOptions<AppDbContext> options, ICpfProtector? cpfProtector = null)
        : base(options, ClinicaContextFactory.CreatePlatform(), cpfProtector)
    {
    }
}
