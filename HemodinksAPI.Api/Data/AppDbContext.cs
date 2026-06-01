using HemodinksAPI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Data;

/// <summary>
/// Contexto de banco de dados da aplicação
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// DbSet de usuários
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Configuração do modelo de dados
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuração da tabela Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Telefone)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Senha)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.DataNascimento)
                .IsRequired();

            entity.Property(e => e.Ativo)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.PrecisaTrocarSenha)
                .IsRequired()
                .HasDefaultValue(true);

            // Índice único para email
            entity.HasIndex(e => e.Email)
                .IsUnique();

            // Índice para telefone
            entity.HasIndex(e => e.Telefone);
        });
    }
}
