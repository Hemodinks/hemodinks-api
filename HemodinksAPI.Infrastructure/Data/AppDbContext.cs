using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Contexto de banco de dados da aplicação
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    /// <summary>
    /// DbSet de usuários
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Perfil> Perfis { get; set; } = null!;

    public DbSet<Paciente> Pacientes { get; set; } = null!;

    public DbSet<FaturamentoMedico> FaturamentosMedicos { get; set; } = null!;

    public DbSet<Observacao> Observacoes { get; set; } = null!;

    public DbSet<GrupoMedico> GruposMedicos { get; set; } = null!;

    public DbSet<GrupoMedicoUsuario> GrupoMedicoUsuarios { get; set; } = null!;

    public DbSet<Hospital> Hospitais { get; set; } = null!;

    public DbSet<Convenio> Convenios { get; set; } = null!;

    public DbSet<Opme> OPME { get; set; } = null!;

    public DbSet<PacienteArquivo> PacienteArquivos { get; set; } = null!;

    public DbSet<PacienteProcedimento> PacienteProcedimentos { get; set; } = null!;

    public DbSet<UserArquivo> UserArquivos { get; set; } = null!;

    public DbSet<CbhpmGeral> CbhpmGeral { get; set; } = null!;

    public DbSet<Licenca> Licencas { get; set; } = null!;

    public DbSet<Event> Events { get; set; } = null!;

    public DbSet<AgendaNotification> AgendaNotifications { get; set; } = null!;

    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;

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
        modelBuilder.Entity<Perfil>(entity =>
        {
            entity.ToTable("Perfis");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Nome)
                .IsUnique();

            entity.HasData(
                new Perfil { Id = Perfil.AdministradorId, Nome = "Administrador" },
                new Perfil { Id = Perfil.MedicosId, Nome = "Médicos" },
                new Perfil { Id = Perfil.PacientesId, Nome = "Pacientes" },
                new Perfil { Id = Perfil.ControllerId, Nome = "Controller" });
        });

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

            entity.Property(e => e.Cpf)
                .HasMaxLength(11);

            entity.Property(e => e.Crm)
                .HasMaxLength(20);

            entity.Property(e => e.CrmUf)
                .HasMaxLength(2);

            entity.Property(e => e.FotoPerfil)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.Senha)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.DataAtualizacao);

            entity.Property(e => e.DataNascimento)
                .IsRequired();

            entity.Property(e => e.Ativo)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.PrecisaTrocarSenha)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.PerfilId)
                .IsRequired()
                .HasDefaultValue(Perfil.MedicosId);

            // Índice único para email
            entity.HasIndex(e => e.Email)
                .IsUnique();

            // Índice para telefone
            entity.HasIndex(e => e.Telefone);

            entity.HasIndex(e => e.Cpf)
                .IsUnique()
                .HasFilter("[Cpf] IS NOT NULL");

            entity.HasIndex(e => e.PerfilId);

            entity.HasOne(e => e.Perfil)
                .WithMany(e => e.Users)
                .HasForeignKey(e => e.PerfilId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Arquivos)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Licenca)
                .WithOne(e => e.User)
                .HasForeignKey<Licenca>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.GruposMedicos)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ObservacoesEnviadas)
                .WithOne(e => e.AutorUser)
                .HasForeignKey(e => e.AutorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.ObservacoesRecebidas)
                .WithOne(e => e.DestinatarioUser)
                .HasForeignKey(e => e.DestinatarioUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GrupoMedico>(entity =>
        {
            entity.ToTable("GruposMedicos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Ativo)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.DataAtualizacao);

            entity.HasIndex(e => e.Nome)
                .IsUnique();
        });

        modelBuilder.Entity<GrupoMedicoUsuario>(entity =>
        {
            entity.ToTable("GrupoMedicoUsuarios");

            entity.HasKey(e => new { e.GrupoMedicoId, e.UserId });

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.GrupoMedico)
                .WithMany(e => e.Membros)
                .HasForeignKey(e => e.GrupoMedicoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(e => e.GruposMedicos)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Licenca>(entity =>
        {
            entity.ToTable("Licencas");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Plano)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(e => e.DataInicioTrial)
                .IsRequired();

            entity.Property(e => e.DataFimTrial)
                .IsRequired();

            entity.Property(e => e.FeaturesLiberadas)
                .HasMaxLength(1000);

            entity.Property(e => e.Observacoes)
                .HasMaxLength(1000);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.UserId)
                .IsUnique();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Description)
                .HasMaxLength(2000);

            entity.Property(e => e.Start)
                .IsRequired();

            entity.Property(e => e.End)
                .IsRequired();

            entity.Property(e => e.NotifyMedicalProfile)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.NotifyUser)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.IsCompleted)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.NextReminderAt);

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.UserId);

            entity.HasIndex(e => e.MedicalUserId);

            entity.HasIndex(e => new { e.Start, e.End, e.IsCompleted });

            entity.HasIndex(e => new { e.NextReminderAt, e.IsCompleted });

            entity.HasOne(e => e.User)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MedicalUser)
                .WithMany(e => e.MedicalEvents)
                .HasForeignKey(e => e.MedicalUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgendaNotification>(entity =>
        {
            entity.ToTable("AgendaNotifications");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.ReadAt);

            entity.HasIndex(e => new { e.RecipientUserId, e.ReadAt, e.CreatedAt });
            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.SenderUserId);

            entity.HasOne(e => e.Event)
                .WithMany()
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SenderUser)
                .WithMany()
                .HasForeignKey(e => e.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RecipientUser)
                .WithMany()
                .HasForeignKey(e => e.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("PasswordResetTokens");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.ExpiresAt)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.RequestIp)
                .HasMaxLength(45);

            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            entity.HasIndex(e => new { e.UserId, e.UsedAt, e.ExpiresAt });

            entity.HasOne(e => e.User)
                .WithMany(e => e.PasswordResetTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Observacao>(entity =>
        {
            entity.ToTable("Observacoes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Texto)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.Medico)
                .HasMaxLength(255);

            entity.Property(e => e.MedicoAuxiliar1)
                .HasMaxLength(255);

            entity.Property(e => e.MedicoAuxiliar2)
                .HasMaxLength(255);

            entity.HasIndex(e => new { e.PacienteId, e.DataCadastro });

            entity.HasIndex(e => new { e.DestinatarioUserId, e.DataLeitura, e.DataCadastro });

            entity.HasIndex(e => new { e.AutorUserId, e.DataCadastro });

            entity.HasOne(e => e.Paciente)
                .WithMany(e => e.Observacoes)
                .HasForeignKey(e => e.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ObservacaoPai)
                .WithMany()
                .HasForeignKey(e => e.ObservacaoPaiId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Hospital>(entity =>
        {
            entity.ToTable("Hospitais");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(e => e.Nome)
                .IsUnique();

            entity.HasData(
                new Hospital { Id = 1, Nome = "Santa Clara - Mater Dei" },
                new Hospital { Id = 2, Nome = "Santa Genoveva - Mater Dei" },
                new Hospital { Id = 3, Nome = "UMC - Complexo Hospitalar" });
        });

        modelBuilder.Entity<Convenio>(entity =>
        {
            entity.ToTable("Convenios");

            entity.HasKey(e => e.IdConvenio);

            entity.Property(e => e.DescricaoConvenio)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(e => e.DescricaoConvenio)
                .IsUnique();

            entity.HasData(
                new Convenio { IdConvenio = 1, DescricaoConvenio = "Amil" },
                new Convenio { IdConvenio = 2, DescricaoConvenio = "Bradesco Sa\u00fade" },
                new Convenio { IdConvenio = 3, DescricaoConvenio = "Cemig Sa\u00fade" },
                new Convenio { IdConvenio = 4, DescricaoConvenio = "Fusex" },
                new Convenio { IdConvenio = 5, DescricaoConvenio = "Geap" },
                new Convenio { IdConvenio = 6, DescricaoConvenio = "Ipsemg" },
                new Convenio { IdConvenio = 7, DescricaoConvenio = "Particular" },
                new Convenio { IdConvenio = 8, DescricaoConvenio = "Sul Am\u00e9rica" },
                new Convenio { IdConvenio = 9, DescricaoConvenio = "Unimed Uberl\u00e2ndia - Plano  Unimed Interc\u00e2mbio" });
        });

        modelBuilder.Entity<Opme>(entity =>
        {
            entity.ToTable("OPME");

            entity.HasKey(e => e.IdFornecedor);

            entity.Property(e => e.IdFornecedor)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Fornecedor)
                .IsRequired()
                .HasMaxLength(255);

            entity.HasIndex(e => e.Fornecedor)
                .IsUnique();

            entity.HasData(
                new Opme { IdFornecedor = 1, Fornecedor = "Promedom" },
                new Opme { IdFornecedor = 2, Fornecedor = "AVL" },
                new Opme { IdFornecedor = 3, Fornecedor = "GE" },
                new Opme { IdFornecedor = 4, Fornecedor = "Spyner" });
        });

        modelBuilder.Entity<Paciente>(entity =>
        {
            entity.ToTable("Pacientes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.NomePaciente)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Diagnostico)
                .HasMaxLength(100);

            entity.Property(e => e.TratamentoMedico)
                .HasMaxLength(100);

            entity.Property(e => e.Hospital)
                .HasMaxLength(255);

            entity.Property(e => e.Medico)
                .HasMaxLength(255);

            entity.HasIndex(e => e.MedicoUserId);

            entity.Property(e => e.MedicoAuxiliar1)
                .HasMaxLength(255);

            entity.HasIndex(e => e.MedicoAuxiliar1UserId);

            entity.Property(e => e.MedicoAuxiliar2)
                .HasMaxLength(255);

            entity.HasIndex(e => e.MedicoAuxiliar2UserId);

            entity.Property(e => e.Convenio)
                .HasMaxLength(255);

            entity.HasIndex(e => e.ConvenioId);

            entity.Property(e => e.OpmeFornecedor)
                .HasMaxLength(255);

            entity.HasIndex(e => e.OpmeFornecedorId);

            entity.Property(e => e.CbhpmCodigo)
                .HasMaxLength(20);

            entity.Property(e => e.CbhpmPorte)
                .HasMaxLength(10);

            entity.Property(e => e.Procedimento)
                .HasMaxLength(1000);

            entity.Property(e => e.Autorizacao)
                .HasMaxLength(255);

            entity.Property(e => e.Pagamento)
                .HasMaxLength(255);

            entity.Property(e => e.RepasseGlosa)
                .HasMaxLength(255);

            entity.Property(e => e.StatusPago)
                .IsRequired()
                .HasDefaultValue(false);

            entity.HasIndex(e => e.UserId)
                .IsUnique();

            entity.HasIndex(e => e.CbhpmCodigo);

            entity.HasIndex(e => e.HospitalId);

            entity.HasOne(e => e.User)
                .WithOne(e => e.Paciente)
                .HasForeignKey<Paciente>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.HospitalReferencia)
                .WithMany(e => e.Pacientes)
                .HasForeignKey(e => e.HospitalId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MedicoUser)
                .WithMany()
                .HasForeignKey(e => e.MedicoUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MedicoAuxiliar1User)
                .WithMany()
                .HasForeignKey(e => e.MedicoAuxiliar1UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MedicoAuxiliar2User)
                .WithMany()
                .HasForeignKey(e => e.MedicoAuxiliar2UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ConvenioReferencia)
                .WithMany(e => e.Pacientes)
                .HasForeignKey(e => e.ConvenioId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.OpmeFornecedorReferencia)
                .WithMany(e => e.Pacientes)
                .HasForeignKey(e => e.OpmeFornecedorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Procedimentos)
                .WithOne(e => e.Paciente)
                .HasForeignKey(e => e.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FaturamentoMedico)
                .WithOne(e => e.Paciente)
                .HasForeignKey<FaturamentoMedico>(e => e.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FaturamentoMedico>(entity =>
        {
            entity.ToTable("FaturamentosMedicos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.HonorariosCirurgiao)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.HonorariosAuxiliares)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.HonorariosAnestesista)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.AnestesistaFaturadoSeparado)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.Anestesista)
                .HasMaxLength(255);

            entity.Property(e => e.CodigoTussCbhpmAmb)
                .HasMaxLength(1000);

            entity.Property(e => e.PorteCirurgicoAnestesico)
                .HasMaxLength(255);

            entity.Property(e => e.GuiaAutorizacaoConvenio)
                .HasMaxLength(255);

            entity.Property(e => e.GuiaInternacaoOuSadt)
                .HasMaxLength(255);

            entity.Property(e => e.OpmeMateriaisEspeciais)
                .HasMaxLength(255);

            entity.Property(e => e.TissXmlStatus)
                .HasMaxLength(255);

            entity.Property(e => e.ValorGlosa)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.GlosaStatus)
                .HasMaxLength(255);

            entity.Property(e => e.RecursoGlosa)
                .HasMaxLength(1000);

            entity.Property(e => e.ConferenciaPagamentoRealizada)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.RepasseMedico)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.RepasseMedicoObservacao)
                .HasMaxLength(1000);

            entity.Property(e => e.TipoFaturamentoParticular)
                .HasMaxLength(100);

            entity.Property(e => e.ReciboNotaContrato)
                .HasMaxLength(255);

            entity.Property(e => e.Observacoes)
                .HasMaxLength(2000);

            entity.Property(e => e.DataCadastro)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.DataAtualizacao);

            entity.HasIndex(e => e.PacienteId)
                .IsUnique();

            entity.HasIndex(e => e.ConferenciaPagamentoRealizada);
        });

        modelBuilder.Entity<PacienteProcedimento>(entity =>
        {
            entity.ToTable("PacienteProcedimentos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.CbhpmCodigo)
                .HasMaxLength(20);

            entity.Property(e => e.CbhpmPorte)
                .HasMaxLength(10);

            entity.Property(e => e.Procedimento)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.ValorReferencia)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Ordem)
                .IsRequired()
                .HasDefaultValue(1);

            entity.HasIndex(e => e.PacienteId);

            entity.HasIndex(e => e.CbhpmCodigo);
        });

        modelBuilder.Entity<PacienteArquivo>(entity =>
        {
            entity.ToTable("PacienteArquivos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.NomeOriginal)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.ContentType)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.Url)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.DataUpload)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.PacienteId);

            entity.HasOne(e => e.Paciente)
                .WithMany(e => e.Arquivos)
                .HasForeignKey(e => e.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserArquivo>(entity =>
        {
            entity.ToTable("UserArquivos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.NomeOriginal)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.ContentType)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.Url)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.DataUpload)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany(e => e.Arquivos)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CbhpmGeral>(entity =>
        {
            entity.ToTable("CBHPMGeral");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Codigo)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Procedimento)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.Porte)
                .HasMaxLength(10);

            entity.Property(e => e.CustoOperacional)
                .HasColumnType("decimal(18,3)");

            entity.Property(e => e.ValorReferencia)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Capitulo)
                .HasMaxLength(255);

            entity.Property(e => e.Grupo)
                .HasMaxLength(255);

            entity.HasIndex(e => e.Codigo)
                .IsUnique();

            entity.HasIndex(e => e.Porte);
        });
    }
}
