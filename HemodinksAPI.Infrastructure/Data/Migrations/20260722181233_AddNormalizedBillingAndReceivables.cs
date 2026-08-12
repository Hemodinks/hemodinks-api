using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedBillingAndReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtendimentosCirurgicos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    DataProcedimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HospitalId = table.Column<int>(type: "int", nullable: true),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    MedicoResponsavelId = table.Column<int>(type: "int", nullable: false),
                    MedicoAuxiliar1Id = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar2Id = table.Column<int>(type: "int", nullable: true),
                    Diagnostico = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TratamentoMedico = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NumeroAutorizacao = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentosCirurgicos", x => x.Id);
                    table.CheckConstraint("CK_AtendimentosCirurgicos_MedicosDistintos", "([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar1Id] <> [MedicoResponsavelId]) AND ([MedicoAuxiliar2Id] IS NULL OR ([MedicoAuxiliar2Id] <> [MedicoResponsavelId] AND ([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar2Id] <> [MedicoAuxiliar1Id])))");
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Hospitais_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar1Id",
                        column: x => x.MedicoAuxiliar1Id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar2Id",
                        column: x => x.MedicoAuxiliar2Id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoResponsavelId",
                        column: x => x.MedicoResponsavelId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConvenioProcedimentoPrecos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValorNegociado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PercentualPrincipal = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    PercentualAuxiliar1 = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    PercentualAuxiliar2 = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    VigenciaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VigenciaFinal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvenioProcedimentoPrecos", x => x.Id);
                    table.CheckConstraint("CK_ConvenioProcedimentoPrecos_Valores", "[ValorNegociado] >= 0 AND [PercentualPrincipal] >= 0 AND [PercentualAuxiliar1] >= 0 AND [PercentualAuxiliar2] >= 0");
                    table.CheckConstraint("CK_ConvenioProcedimentoPrecos_Vigencia", "[VigenciaFinal] IS NULL OR [VigenciaFinal] >= [VigenciaInicio]");
                    table.ForeignKey(
                        name: "FK_ConvenioProcedimentoPrecos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConvenioProcedimentoPrecos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AtendimentoProcedimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CbhpmPorte = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PesoPercentual = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ValorReferencia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValorNegociado = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentoProcedimentos", x => x.Id);
                    table.CheckConstraint("CK_AtendimentoProcedimentos_PesoPercentual", "[PesoPercentual] >= 0");
                    table.CheckConstraint("CK_AtendimentoProcedimentos_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_AtendimentoProcedimentos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AtendimentoProcedimentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Faturamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    NumeroGuia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NumeroLote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Competencia = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataEnvio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataRetorno = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValorApresentado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosaRecuperada = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorReconhecido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faturamentos", x => x.Id);
                    table.CheckConstraint("CK_Faturamentos_Valores", "[ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorGlosaRecuperada] >= 0 AND [ValorReconhecido] >= 0");
                    table.ForeignKey(
                        name: "FK_Faturamentos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContasReceber",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Competencia = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorOriginal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorAjustado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorRecebido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SaldoAberto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasReceber", x => x.Id);
                    table.CheckConstraint("CK_ContasReceber_Valores", "[ValorOriginal] >= 0 AND [ValorAjustado] >= 0 AND [ValorRecebido] >= 0 AND [SaldoAberto] >= 0");
                    table.ForeignKey(
                        name: "FK_ContasReceber_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaturamentoItens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoProcedimentoId = table.Column<int>(type: "int", nullable: true),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PesoPercentual = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorApresentado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorAprovado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MotivoGlosa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentoItens", x => x.Id);
                    table.CheckConstraint("CK_FaturamentoItens_Valores", "[Quantidade] > 0 AND [PesoPercentual] >= 0 AND [ValorUnitario] >= 0 AND [ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorAprovado] >= 0");
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_AtendimentoProcedimentos_AtendimentoProcedimentoId",
                        column: x => x.AtendimentoProcedimentoId,
                        principalTable: "AtendimentoProcedimentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recebimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    ContaReceberId = table.Column<int>(type: "int", nullable: false),
                    DataRecebimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorRecebido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FormaRecebimento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenciaBancaria = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DocumentoComprovante = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UsuarioCadastroId = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Estornado = table.Column<bool>(type: "bit", nullable: false),
                    DataEstorno = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioEstornoId = table.Column<int>(type: "int", nullable: true),
                    MotivoEstorno = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recebimentos", x => x.Id);
                    table.CheckConstraint("CK_Recebimentos_Valor", "[ValorRecebido] > 0");
                    table.ForeignKey(
                        name: "FK_Recebimentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_ContasReceber_ContaReceberId",
                        column: x => x.ContaReceberId,
                        principalTable: "ContasReceber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_Users_UsuarioCadastroId",
                        column: x => x.UsuarioCadastroId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_Users_UsuarioEstornoId",
                        column: x => x.UsuarioEstornoId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Glosas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoItemId = table.Column<int>(type: "int", nullable: true),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DescricaoMotivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataGlosa = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Glosas", x => x.Id);
                    table.CheckConstraint("CK_Glosas_Valor", "[ValorGlosado] > 0");
                    table.ForeignKey(
                        name: "FK_Glosas_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Glosas_FaturamentoItens_FaturamentoItemId",
                        column: x => x.FaturamentoItemId,
                        principalTable: "FaturamentoItens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Glosas_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecursosGlosa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    GlosaId = table.Column<int>(type: "int", nullable: false),
                    DataEnvio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Justificativa = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ValorRecorrido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataResposta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValorRecuperado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecursosGlosa", x => x.Id);
                    table.CheckConstraint("CK_RecursosGlosa_Valores", "[ValorRecorrido] > 0 AND [ValorRecuperado] >= 0 AND [ValorRecuperado] <= [ValorRecorrido]");
                    table.ForeignKey(
                        name: "FK_RecursosGlosa_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecursosGlosa_Glosas_GlosaId",
                        column: x => x.GlosaId,
                        principalTable: "Glosas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoProcedimentos_AtendimentoCirurgicoId",
                table: "AtendimentoProcedimentos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoProcedimentos_ClinicaId_AtendimentoCirurgicoId_Ordem",
                table: "AtendimentoProcedimentos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_PacienteId_DataProcedimento",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "PacienteId", "DataProcedimento" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ConvenioId",
                table: "AtendimentosCirurgicos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_HospitalId",
                table: "AtendimentosCirurgicos",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar1Id");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar2Id");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                column: "MedicoResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_PacienteId",
                table: "AtendimentosCirurgicos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_FaturamentoId_DataVencimento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "FaturamentoId", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_NumeroDocumento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "NumeroDocumento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_Status_DataVencimento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "Status", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ConvenioId",
                table: "ContasReceber",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_FaturamentoId",
                table: "ContasReceber",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_PacienteId",
                table: "ContasReceber",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ClinicaId_ConvenioId_CbhpmCodigo_Ativo",
                table: "ConvenioProcedimentoPrecos",
                columns: new[] { "ClinicaId", "ConvenioId", "CbhpmCodigo", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ClinicaId_ConvenioId_CbhpmCodigo_VigenciaInicio",
                table: "ConvenioProcedimentoPrecos",
                columns: new[] { "ClinicaId", "ConvenioId", "CbhpmCodigo", "VigenciaInicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ConvenioId",
                table: "ConvenioProcedimentoPrecos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_AtendimentoProcedimentoId",
                table: "FaturamentoItens",
                column: "AtendimentoProcedimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_ClinicaId_FaturamentoId_Ordem",
                table: "FaturamentoItens",
                columns: new[] { "ClinicaId", "FaturamentoId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_FaturamentoId",
                table: "FaturamentoItens",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_AtendimentoCirurgicoId",
                table: "Faturamentos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ClinicaId_AtendimentoCirurgicoId_Competencia",
                table: "Faturamentos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId", "Competencia" });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ClinicaId_NumeroGuia",
                table: "Faturamentos",
                columns: new[] { "ClinicaId", "NumeroGuia" });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ConvenioId",
                table: "Faturamentos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_ClinicaId_FaturamentoId_Status",
                table: "Glosas",
                columns: new[] { "ClinicaId", "FaturamentoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_FaturamentoId",
                table: "Glosas",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_FaturamentoItemId",
                table: "Glosas",
                column: "FaturamentoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ClinicaId_ContaReceberId_Estornado",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "ContaReceberId", "Estornado" });

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ContaReceberId",
                table: "Recebimentos",
                column: "ContaReceberId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioCadastroId",
                table: "Recebimentos",
                column: "UsuarioCadastroId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioEstornoId",
                table: "Recebimentos",
                column: "UsuarioEstornoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecursosGlosa_ClinicaId_GlosaId_Status",
                table: "RecursosGlosa",
                columns: new[] { "ClinicaId", "GlosaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecursosGlosa_GlosaId",
                table: "RecursosGlosa",
                column: "GlosaId");

            // Transicao aditiva: preserva as tabelas legadas e copia somente registros com
            // medico e data validos. A migration e idempotente em relacao ao backfill.
            migrationBuilder.Sql("""
                INSERT INTO AtendimentosCirurgicos
                    (ClinicaId, PacienteId, DataProcedimento, HospitalId, ConvenioId,
                     MedicoResponsavelId, MedicoAuxiliar1Id, MedicoAuxiliar2Id,
                     Diagnostico, TratamentoMedico, NumeroAutorizacao, Status, DataCadastro, DataAtualizacao)
                SELECT p.ClinicaId, p.Id, p.Data, p.HospitalId, p.ConvenioId,
                       p.MedicoUserId,
                       CASE WHEN p.MedicoAuxiliar1UserId = p.MedicoUserId THEN NULL ELSE p.MedicoAuxiliar1UserId END,
                       CASE WHEN p.MedicoAuxiliar2UserId = p.MedicoUserId OR p.MedicoAuxiliar2UserId = p.MedicoAuxiliar1UserId
                            THEN NULL ELSE p.MedicoAuxiliar2UserId END,
                       p.Diagnostico, p.TratamentoMedico, p.Autorizacao, 'Realizado',
                       COALESCE(u.DataCadastro, GETUTCDATE()), u.DataAtualizacao
                FROM Pacientes p
                INNER JOIN Users u ON u.Id = p.UserId AND u.ClinicaId = p.ClinicaId
                WHERE p.Data IS NOT NULL AND p.MedicoUserId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM AtendimentosCirurgicos a
                                  WHERE a.ClinicaId = p.ClinicaId AND a.PacienteId = p.Id);

                INSERT INTO AtendimentoProcedimentos
                    (ClinicaId, AtendimentoCirurgicoId, CbhpmCodigo, CbhpmPorte, Descricao,
                     Quantidade, PesoPercentual, ValorReferencia, ValorNegociado, Ordem, DataCadastro)
                SELECT pp.ClinicaId, a.Id, pp.CbhpmCodigo, pp.CbhpmPorte, pp.Procedimento,
                       1, 100, pp.ValorReferencia, NULL, pp.Ordem, a.DataCadastro
                FROM PacienteProcedimentos pp
                INNER JOIN AtendimentosCirurgicos a ON a.PacienteId = pp.PacienteId AND a.ClinicaId = pp.ClinicaId
                WHERE NOT EXISTS (SELECT 1 FROM AtendimentoProcedimentos ap
                                  WHERE ap.AtendimentoCirurgicoId = a.Id AND ap.Ordem = pp.Ordem);

                INSERT INTO AtendimentoProcedimentos
                    (ClinicaId, AtendimentoCirurgicoId, CbhpmCodigo, CbhpmPorte, Descricao,
                     Quantidade, PesoPercentual, ValorReferencia, ValorNegociado, Ordem, DataCadastro)
                SELECT p.ClinicaId, a.Id, p.CbhpmCodigo, p.CbhpmPorte, p.Procedimento,
                       1, 100, NULL, NULL, 1, a.DataCadastro
                FROM Pacientes p
                INNER JOIN AtendimentosCirurgicos a ON a.PacienteId = p.Id AND a.ClinicaId = p.ClinicaId
                WHERE p.Procedimento IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM AtendimentoProcedimentos ap WHERE ap.AtendimentoCirurgicoId = a.Id);

                INSERT INTO Faturamentos
                    (ClinicaId, AtendimentoCirurgicoId, ConvenioId, NumeroGuia, NumeroLote, Competencia,
                     DataEnvio, DataRetorno, ValorApresentado, ValorGlosado, ValorGlosaRecuperada,
                     ValorReconhecido, Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT fm.ClinicaId, a.Id, p.ConvenioId,
                       COALESCE(fm.GuiaInternacaoOuSadt, fm.GuiaAutorizacaoConvenio), NULL,
                       COALESCE(fm.CompetenciaInicio, DATEFROMPARTS(YEAR(a.DataProcedimento), MONTH(a.DataProcedimento), 1)),
                       NULL, CASE WHEN fm.ConferenciaPagamentoRealizada = 1 THEN fm.DataAtualizacao ELSE NULL END,
                       COALESCE(fm.HonorariosCirurgiao, 0) + COALESCE(fm.HonorariosAuxiliares, 0) + COALESCE(fm.HonorariosAnestesista, 0),
                       COALESCE(fm.ValorGlosa, 0), 0,
                       CASE WHEN COALESCE(fm.ValorGlosa, 0) > COALESCE(fm.HonorariosCirurgiao, 0) + COALESCE(fm.HonorariosAuxiliares, 0) + COALESCE(fm.HonorariosAnestesista, 0)
                            THEN 0 ELSE COALESCE(fm.HonorariosCirurgiao, 0) + COALESCE(fm.HonorariosAuxiliares, 0) + COALESCE(fm.HonorariosAnestesista, 0) - COALESCE(fm.ValorGlosa, 0) END,
                       CASE WHEN fm.ConferenciaPagamentoRealizada = 1 THEN 'Pago'
                            WHEN COALESCE(fm.ValorGlosa, 0) > 0 THEN 'GlosadoParcial' ELSE 'Rascunho' END,
                       CONCAT('Migrado do faturamento medico legado #', fm.Id, '. ', COALESCE(fm.Observacoes, '')),
                       fm.DataCadastro, fm.DataAtualizacao
                FROM FaturamentosMedicos fm
                INNER JOIN Pacientes p ON p.Id = fm.PacienteId AND p.ClinicaId = fm.ClinicaId
                INNER JOIN AtendimentosCirurgicos a ON a.PacienteId = p.Id AND a.ClinicaId = p.ClinicaId
                WHERE NOT EXISTS (SELECT 1 FROM Faturamentos f
                                  WHERE f.AtendimentoCirurgicoId = a.Id AND f.ClinicaId = fm.ClinicaId);

                INSERT INTO FaturamentoItens
                    (ClinicaId, FaturamentoId, AtendimentoProcedimentoId, Codigo, Descricao,
                     Quantidade, PesoPercentual, ValorUnitario, ValorApresentado, ValorGlosado,
                     ValorAprovado, MotivoGlosa, Status, Ordem, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, NULL, NULL, 'Valor consolidado do faturamento legado',
                       1, 100, f.ValorApresentado, f.ValorApresentado, f.ValorGlosado,
                       f.ValorReconhecido, NULL,
                       CASE WHEN f.ValorGlosado >= f.ValorApresentado AND f.ValorApresentado > 0 THEN 'GlosadoTotal'
                            WHEN f.ValorGlosado > 0 THEN 'GlosadoParcial' ELSE 'Apresentado' END,
                       1, f.DataCadastro, f.DataAtualizacao
                FROM Faturamentos f
                WHERE NOT EXISTS (SELECT 1 FROM FaturamentoItens fi WHERE fi.FaturamentoId = f.Id);

                INSERT INTO Glosas
                    (ClinicaId, FaturamentoId, FaturamentoItemId, CodigoMotivo, DescricaoMotivo,
                     ValorGlosado, DataGlosa, Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, NULL, NULL, COALESCE(fm.GlosaStatus, 'Glosa migrada do legado'),
                       fm.ValorGlosa, COALESCE(fm.DataAtualizacao, fm.DataCadastro), 'Aberta',
                       fm.RecursoGlosa, fm.DataCadastro, fm.DataAtualizacao
                FROM FaturamentosMedicos fm
                INNER JOIN AtendimentosCirurgicos a ON a.PacienteId = fm.PacienteId AND a.ClinicaId = fm.ClinicaId
                INNER JOIN Faturamentos f ON f.AtendimentoCirurgicoId = a.Id AND f.ClinicaId = fm.ClinicaId
                WHERE fm.ValorGlosa > 0
                  AND NOT EXISTS (SELECT 1 FROM Glosas g WHERE g.FaturamentoId = f.Id);

                INSERT INTO ContasReceber
                    (ClinicaId, FaturamentoId, ConvenioId, PacienteId, NumeroDocumento, Descricao,
                     Competencia, DataEmissao, DataVencimento, ValorOriginal, ValorAjustado,
                     ValorRecebido, SaldoAberto, Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, f.ConvenioId, a.PacienteId,
                       CONCAT('LEG-FAT-', f.Id), 'Titulo migrado do faturamento medico legado',
                       f.Competencia, f.DataCadastro, DATEADD(DAY, 30, f.DataCadastro),
                       f.ValorApresentado, f.ValorReconhecido,
                       CASE WHEN fm.ConferenciaPagamentoRealizada = 1 THEN f.ValorReconhecido ELSE 0 END,
                       CASE WHEN fm.ConferenciaPagamentoRealizada = 1 THEN 0 ELSE f.ValorReconhecido END,
                       CASE WHEN fm.ConferenciaPagamentoRealizada = 1 THEN 'Recebido'
                            WHEN DATEADD(DAY, 30, f.DataCadastro) < GETUTCDATE() THEN 'Vencido' ELSE 'Aberto' END,
                       'Origem: FaturamentosMedicos. Revisar antes da conciliacao.', f.DataCadastro, f.DataAtualizacao
                FROM Faturamentos f
                INNER JOIN AtendimentosCirurgicos a ON a.Id = f.AtendimentoCirurgicoId
                INNER JOIN FaturamentosMedicos fm ON fm.PacienteId = a.PacienteId AND fm.ClinicaId = f.ClinicaId
                WHERE f.ValorApresentado > 0
                  AND NOT EXISTS (SELECT 1 FROM ContasReceber c WHERE c.FaturamentoId = f.Id AND c.NumeroDocumento = CONCAT('LEG-FAT-', f.Id));

                INSERT INTO Recebimentos
                    (ClinicaId, ContaReceberId, DataRecebimento, ValorRecebido, FormaRecebimento,
                     ReferenciaBancaria, DocumentoComprovante, Observacao, UsuarioCadastroId,
                     DataCadastro, Estornado, DataEstorno, UsuarioEstornoId, MotivoEstorno)
                SELECT c.ClinicaId, c.Id, COALESCE(fm.DataAtualizacao, fm.DataCadastro), c.ValorRecebido,
                       'Outro', NULL, NULL, 'Recebimento migrado de conferencia legada; requer conciliacao.',
                       p.UserId, COALESCE(fm.DataAtualizacao, fm.DataCadastro), 0, NULL, NULL, NULL
                FROM ContasReceber c
                INNER JOIN Faturamentos f ON f.Id = c.FaturamentoId
                INNER JOIN AtendimentosCirurgicos a ON a.Id = f.AtendimentoCirurgicoId
                INNER JOIN Pacientes p ON p.Id = a.PacienteId
                INNER JOIN FaturamentosMedicos fm ON fm.PacienteId = p.Id AND fm.ClinicaId = c.ClinicaId
                WHERE fm.ConferenciaPagamentoRealizada = 1 AND c.ValorRecebido > 0
                  AND NOT EXISTS (SELECT 1 FROM Recebimentos r WHERE r.ContaReceberId = c.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConvenioProcedimentoPrecos");

            migrationBuilder.DropTable(
                name: "Recebimentos");

            migrationBuilder.DropTable(
                name: "RecursosGlosa");

            migrationBuilder.DropTable(
                name: "ContasReceber");

            migrationBuilder.DropTable(
                name: "Glosas");

            migrationBuilder.DropTable(
                name: "FaturamentoItens");

            migrationBuilder.DropTable(
                name: "AtendimentoProcedimentos");

            migrationBuilder.DropTable(
                name: "Faturamentos");

            migrationBuilder.DropTable(
                name: "AtendimentosCirurgicos");
        }
    }
}
