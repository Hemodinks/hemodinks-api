using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturamentosMedicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaturamentosMedicos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    HonorariosCirurgiao = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HonorariosAuxiliares = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HonorariosAnestesista = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AnestesistaFaturadoSeparado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Anestesista = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CodigoTussCbhpmAmb = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PorteCirurgicoAnestesico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GuiaAutorizacaoConvenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GuiaInternacaoOuSadt = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OpmeMateriaisEspeciais = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TissXmlStatus = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ValorGlosa = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GlosaStatus = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RecursoGlosa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConferenciaPagamentoRealizada = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RepasseMedico = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RepasseMedicoObservacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TipoFaturamentoParticular = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReciboNotaContrato = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentosMedicos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaturamentosMedicos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos",
                column: "ConferenciaPagamentoRealizada");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_PacienteId",
                table: "FaturamentosMedicos",
                column: "PacienteId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO [FaturamentosMedicos] (
                    [PacienteId],
                    [HonorariosCirurgiao],
                    [CodigoTussCbhpmAmb],
                    [PorteCirurgicoAnestesico],
                    [GuiaAutorizacaoConvenio],
                    [OpmeMateriaisEspeciais],
                    [ValorGlosa],
                    [GlosaStatus],
                    [ConferenciaPagamentoRealizada],
                    [RepasseMedico],
                    [TipoFaturamentoParticular],
                    [DataCadastro],
                    [DataAtualizacao])
                SELECT
                    p.[Id],
                    parsed.[ValorPago],
                    LEFT(COALESCE(codigos.[Codigos], NULLIF(p.[CbhpmCodigo], '')), 1000),
                    LEFT(COALESCE(portes.[Portes], NULLIF(p.[CbhpmPorte], '')), 255),
                    NULLIF(p.[Autorizacao], ''),
                    NULLIF(COALESCE(NULLIF(p.[OpmeFornecedor], ''), opme.[Fornecedor]), ''),
                    parsed.[ValorGlosa],
                    CASE
                        WHEN parsed.[ValorGlosa] > 0 THEN N'Glosa informada'
                        WHEN NULLIF(p.[RepasseGlosa], '') IS NOT NULL THEN p.[RepasseGlosa]
                        WHEN p.[StatusPago] = CAST(1 AS bit) THEN N'Pagamento conferido'
                        ELSE NULL
                    END,
                    p.[StatusPago],
                    CASE
                        WHEN parsed.[ValorPago] IS NULL AND parsed.[ValorGlosa] IS NULL THEN NULL
                        ELSE COALESCE(parsed.[ValorPago], 0) - COALESCE(parsed.[ValorGlosa], 0)
                    END,
                    CASE
                        WHEN NULLIF(COALESCE(NULLIF(p.[Convenio], ''), convenio.[DescricaoConvenio]), '') IS NULL THEN N'Particular'
                        WHEN UPPER(COALESCE(NULLIF(p.[Convenio], ''), convenio.[DescricaoConvenio])) LIKE N'%PARTICULAR%' THEN N'Particular'
                        ELSE N'Convenio'
                    END,
                    GETUTCDATE(),
                    GETUTCDATE()
                FROM [Pacientes] p
                LEFT JOIN [Convenios] convenio
                    ON convenio.[IdConvenio] = p.[ConvenioId]
                LEFT JOIN [OPME] opme
                    ON opme.[IdFornecedor] = p.[OpmeFornecedorId]
                OUTER APPLY (
                    SELECT
                        TRY_CONVERT(decimal(18, 2), REPLACE(REPLACE(REPLACE(REPLACE(p.[Pagamento], 'R$', ''), ' ', ''), '.', ''), ',', '.')) AS [ValorPago],
                        TRY_CONVERT(decimal(18, 2), REPLACE(REPLACE(REPLACE(REPLACE(p.[RepasseGlosa], 'R$', ''), ' ', ''), '.', ''), ',', '.')) AS [ValorGlosa]
                ) parsed
                OUTER APPLY (
                    SELECT STRING_AGG(pp.[CbhpmCodigo], ', ') AS [Codigos]
                    FROM [PacienteProcedimentos] pp
                    WHERE pp.[PacienteId] = p.[Id]
                        AND NULLIF(pp.[CbhpmCodigo], '') IS NOT NULL
                ) codigos
                OUTER APPLY (
                    SELECT STRING_AGG(pp.[CbhpmPorte], ', ') AS [Portes]
                    FROM [PacienteProcedimentos] pp
                    WHERE pp.[PacienteId] = p.[Id]
                        AND NULLIF(pp.[CbhpmPorte], '') IS NOT NULL
                ) portes
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [FaturamentosMedicos] f
                    WHERE f.[PacienteId] = p.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaturamentosMedicos");
        }
    }
}
