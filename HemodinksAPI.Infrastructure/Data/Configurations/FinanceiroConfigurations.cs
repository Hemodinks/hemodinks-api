using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HemodinksAPI.Infrastructure.Data.Configurations;

internal sealed class AtendimentoCirurgicoConfiguration : IEntityTypeConfiguration<AtendimentoCirurgico>
{
    public void Configure(EntityTypeBuilder<AtendimentoCirurgico> entity)
    {
        entity.ToTable("AtendimentosCirurgicos", table => table.HasCheckConstraint("CK_AtendimentosCirurgicos_MedicosDistintos", "([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar1Id] <> [MedicoResponsavelId]) AND ([MedicoAuxiliar2Id] IS NULL OR ([MedicoAuxiliar2Id] <> [MedicoResponsavelId] AND ([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar2Id] <> [MedicoAuxiliar1Id])))"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ClinicaId).IsRequired();
        entity.Property(x => x.DataProcedimento).IsRequired();
        entity.Property(x => x.Diagnostico).HasMaxLength(1000);
        entity.Property(x => x.TratamentoMedico).HasMaxLength(1000);
        entity.Property(x => x.NumeroAutorizacao).HasMaxLength(255);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.PacienteId, x.DataProcedimento });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Paciente).WithMany(x => x.AtendimentosCirurgicos).HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Hospital).WithMany().HasForeignKey(x => x.HospitalId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Convenio).WithMany().HasForeignKey(x => x.ConvenioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.MedicoResponsavel).WithMany().HasForeignKey(x => x.MedicoResponsavelId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.MedicoAuxiliar1).WithMany().HasForeignKey(x => x.MedicoAuxiliar1Id).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.MedicoAuxiliar2).WithMany().HasForeignKey(x => x.MedicoAuxiliar2Id).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AtendimentoProcedimentoConfiguration : IEntityTypeConfiguration<AtendimentoProcedimento>
{
    public void Configure(EntityTypeBuilder<AtendimentoProcedimento> entity)
    {
        entity.ToTable("AtendimentoProcedimentos", table =>
        {
            table.HasCheckConstraint("CK_AtendimentoProcedimentos_Quantidade", "[Quantidade] > 0");
            table.HasCheckConstraint("CK_AtendimentoProcedimentos_PesoPercentual", "[PesoPercentual] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CbhpmCodigo).HasMaxLength(20);
        entity.Property(x => x.CbhpmPorte).HasMaxLength(20);
        entity.Property(x => x.Descricao).IsRequired().HasMaxLength(1000);
        Money(entity.Property(x => x.Quantidade), "decimal(18,4)");
        Money(entity.Property(x => x.PesoPercentual), "decimal(9,4)");
        Money(entity.Property(x => x.ValorReferencia));
        Money(entity.Property(x => x.ValorNegociado));
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.AtendimentoCirurgicoId, x.Ordem }).IsUnique();
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.AtendimentoCirurgico).WithMany(x => x.Procedimentos).HasForeignKey(x => x.AtendimentoCirurgicoId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void Money(PropertyBuilder<decimal?> property, string type = "decimal(18,2)") => property.HasColumnType(type);
    private static void Money(PropertyBuilder<decimal> property, string type = "decimal(18,2)") => property.HasColumnType(type);
}

internal sealed class FaturamentoConfiguration : IEntityTypeConfiguration<Faturamento>
{
    public void Configure(EntityTypeBuilder<Faturamento> entity)
    {
        entity.ToTable("Faturamentos", table =>
        {
            table.HasCheckConstraint("CK_Faturamentos_Valores", "[ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorGlosaRecuperada] >= 0 AND [ValorReconhecido] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.NumeroGuia).HasMaxLength(100);
        entity.Property(x => x.NumeroLote).HasMaxLength(100);
        foreach (var name in new[] { nameof(Faturamento.ValorApresentado), nameof(Faturamento.ValorGlosado), nameof(Faturamento.ValorGlosaRecuperada), nameof(Faturamento.ValorReconhecido) })
            entity.Property<decimal>(name).HasColumnType("decimal(18,2)");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Observacao).HasMaxLength(2000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.ClinicaId, x.AtendimentoCirurgicoId, x.Competencia });
        entity.HasIndex(x => new { x.ClinicaId, x.NumeroGuia });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.AtendimentoCirurgico).WithMany(x => x.Faturamentos).HasForeignKey(x => x.AtendimentoCirurgicoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Convenio).WithMany().HasForeignKey(x => x.ConvenioId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FaturamentoItemConfiguration : IEntityTypeConfiguration<FaturamentoItem>
{
    public void Configure(EntityTypeBuilder<FaturamentoItem> entity)
    {
        entity.ToTable("FaturamentoItens", table => table.HasCheckConstraint("CK_FaturamentoItens_Valores", "[Quantidade] > 0 AND [PesoPercentual] >= 0 AND [ValorUnitario] >= 0 AND [ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorAprovado] >= 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Codigo).HasMaxLength(20);
        entity.Property(x => x.Descricao).IsRequired().HasMaxLength(1000);
        entity.Property(x => x.Quantidade).HasColumnType("decimal(18,4)");
        entity.Property(x => x.PesoPercentual).HasColumnType("decimal(9,4)");
        entity.Property(x => x.ValorUnitario).HasColumnType("decimal(18,2)");
        entity.Property(x => x.ValorApresentado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.ValorGlosado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.ValorAprovado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.MotivoGlosa).HasMaxLength(1000);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.FaturamentoId, x.Ordem }).IsUnique();
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Faturamento).WithMany(x => x.Itens).HasForeignKey(x => x.FaturamentoId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.AtendimentoProcedimento).WithMany().HasForeignKey(x => x.AtendimentoProcedimentoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GlosaConfiguration : IEntityTypeConfiguration<Glosa>
{
    public void Configure(EntityTypeBuilder<Glosa> entity)
    {
        entity.ToTable("Glosas", table => table.HasCheckConstraint("CK_Glosas_Valor", "[ValorGlosado] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CodigoMotivo).HasMaxLength(50);
        entity.Property(x => x.DescricaoMotivo).IsRequired().HasMaxLength(1000);
        entity.Property(x => x.ValorGlosado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Observacao).HasMaxLength(2000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.FaturamentoId, x.Status });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Faturamento).WithMany(x => x.Glosas).HasForeignKey(x => x.FaturamentoId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.FaturamentoItem).WithMany(x => x.Glosas).HasForeignKey(x => x.FaturamentoItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecursoGlosaConfiguration : IEntityTypeConfiguration<RecursoGlosa>
{
    public void Configure(EntityTypeBuilder<RecursoGlosa> entity)
    {
        entity.ToTable("RecursosGlosa", table => table.HasCheckConstraint("CK_RecursosGlosa_Valores", "[ValorRecorrido] > 0 AND [ValorRecuperado] >= 0 AND [ValorRecuperado] <= [ValorRecorrido]"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Justificativa).IsRequired().HasMaxLength(4000);
        entity.Property(x => x.ValorRecorrido).HasColumnType("decimal(18,2)");
        entity.Property(x => x.ValorRecuperado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Observacao).HasMaxLength(2000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.GlosaId, x.Status });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Glosa).WithMany(x => x.Recursos).HasForeignKey(x => x.GlosaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContaReceberConfiguration : IEntityTypeConfiguration<ContaReceber>
{
    public void Configure(EntityTypeBuilder<ContaReceber> entity)
    {
        entity.ToTable("ContasReceber", table => table.HasCheckConstraint("CK_ContasReceber_Valores", "[ValorOriginal] >= 0 AND [ValorAjustado] >= 0 AND [ValorRecebido] >= 0 AND [SaldoAberto] >= 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.NumeroDocumento).IsRequired().HasMaxLength(100);
        entity.Property(x => x.Descricao).IsRequired().HasMaxLength(500);
        foreach (var name in new[] { nameof(ContaReceber.ValorOriginal), nameof(ContaReceber.ValorAjustado), nameof(ContaReceber.ValorRecebido), nameof(ContaReceber.SaldoAberto) })
            entity.Property<decimal>(name).HasColumnType("decimal(18,2)");
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.Observacao).HasMaxLength(2000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.ClinicaId, x.NumeroDocumento }).IsUnique();
        entity.HasIndex(x => new { x.ClinicaId, x.FaturamentoId, x.DataVencimento });
        entity.HasIndex(x => new { x.ClinicaId, x.Status, x.DataVencimento });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Faturamento).WithMany(x => x.ContasReceber).HasForeignKey(x => x.FaturamentoId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Convenio).WithMany().HasForeignKey(x => x.ConvenioId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Paciente).WithMany(x => x.ContasReceber).HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecebimentoConfiguration : IEntityTypeConfiguration<Recebimento>
{
    public void Configure(EntityTypeBuilder<Recebimento> entity)
    {
        entity.ToTable("Recebimentos", table => table.HasCheckConstraint("CK_Recebimentos_Valor", "[ValorRecebido] > 0"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ValorRecebido).HasColumnType("decimal(18,2)");
        entity.Property(x => x.FormaRecebimento).HasConversion<string>().HasMaxLength(30);
        entity.Property(x => x.ReferenciaBancaria).HasMaxLength(255);
        entity.Property(x => x.DocumentoComprovante).HasMaxLength(1000);
        entity.Property(x => x.Observacao).HasMaxLength(2000);
        entity.Property(x => x.MotivoEstorno).HasMaxLength(1000);
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.ContaReceberId, x.Estornado });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.ContaReceber).WithMany(x => x.Recebimentos).HasForeignKey(x => x.ContaReceberId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.UsuarioCadastro).WithMany().HasForeignKey(x => x.UsuarioCadastroId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.UsuarioEstorno).WithMany().HasForeignKey(x => x.UsuarioEstornoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConvenioProcedimentoPrecoConfiguration : IEntityTypeConfiguration<ConvenioProcedimentoPreco>
{
    public void Configure(EntityTypeBuilder<ConvenioProcedimentoPreco> entity)
    {
        entity.ToTable("ConvenioProcedimentoPrecos", table =>
        {
            table.HasCheckConstraint("CK_ConvenioProcedimentoPrecos_Vigencia", "[VigenciaFinal] IS NULL OR [VigenciaFinal] >= [VigenciaInicio]");
            table.HasCheckConstraint("CK_ConvenioProcedimentoPrecos_Valores", "[ValorNegociado] >= 0 AND [PercentualPrincipal] >= 0 AND [PercentualAuxiliar1] >= 0 AND [PercentualAuxiliar2] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.CbhpmCodigo).IsRequired().HasMaxLength(20);
        entity.Property(x => x.ValorNegociado).HasColumnType("decimal(18,2)");
        entity.Property(x => x.PercentualPrincipal).HasColumnType("decimal(9,4)");
        entity.Property(x => x.PercentualAuxiliar1).HasColumnType("decimal(9,4)");
        entity.Property(x => x.PercentualAuxiliar2).HasColumnType("decimal(9,4)");
        entity.Property(x => x.DataCadastro).HasDefaultValueSql("GETUTCDATE()");
        entity.HasIndex(x => new { x.ClinicaId, x.ConvenioId, x.CbhpmCodigo, x.VigenciaInicio }).IsUnique();
        entity.HasIndex(x => new { x.ClinicaId, x.ConvenioId, x.CbhpmCodigo, x.Ativo });
        entity.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Convenio).WithMany().HasForeignKey(x => x.ConvenioId).OnDelete(DeleteBehavior.Restrict);
    }
}
