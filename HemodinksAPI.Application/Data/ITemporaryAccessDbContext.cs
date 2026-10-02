using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Data;

public interface ITemporaryAccessDbContext : IPasswordCredentialDbContext, ICredentialRevocationDbContext
{
    DbSet<TemporaryAccessCredential> TemporaryAccessCredentials { get; }
    DbSet<AuditoriaPlataforma> AuditoriasPlataforma { get; }
}
