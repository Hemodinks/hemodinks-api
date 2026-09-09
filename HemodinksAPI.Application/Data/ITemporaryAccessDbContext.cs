using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Data;

public interface ITemporaryAccessDbContext : IPasswordCredentialDbContext, IPasswordResetDbContext
{
    DbSet<TemporaryAccessCredential> TemporaryAccessCredentials { get; }
    DbSet<AuthenticationSession> AuthenticationSessions { get; }
    DbSet<AuditoriaPlataforma> AuditoriasPlataforma { get; }
}
