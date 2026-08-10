using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyPatientFinancialBackfillAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceiroMigracaoInconsistencias",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    Campo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValorOriginal = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Resolvida = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataResolucao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceiroMigracaoInconsistencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceiroMigracaoInconsistencias_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_ClinicaId_PacienteId_Campo_Resolvida",
                table: "FinanceiroMigracaoInconsistencias",
                columns: new[] { "ClinicaId", "PacienteId", "Campo", "Resolvida" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_ClinicaId_Resolvida_DataCadastro",
                table: "FinanceiroMigracaoInconsistencias",
                columns: new[] { "ClinicaId", "Resolvida", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_PacienteId",
                table: "FinanceiroMigracaoInconsistencias",
                column: "PacienteId");

            migrationBuilder.Sql("""
                WITH Valores AS (
                    SELECT p.*,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.Pagamento, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), NCHAR(160), '') END) AS PagamentoDecimal,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.RepasseGlosa, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), NCHAR(160), '') END) AS GlosaDecimal
                    FROM Pacientes p
                )
                INSERT INTO FinanceiroMigracaoInconsistencias
                    (ClinicaId, PacienteId, Campo, ValorOriginal, Motivo, Resolvida, DataCadastro)
                SELECT ClinicaId, Id, 'Paciente.Pagamento', Pagamento,
                       CASE WHEN PagamentoDecimal < 0 THEN 'Valor monetario legado negativo requer conciliacao.'
                            ELSE 'Valor monetario legado nao pode ser convertido com seguranca.' END, 0, GETUTCDATE()
                FROM Valores v
                WHERE NULLIF(LTRIM(RTRIM(v.Pagamento)), '') IS NOT NULL AND (v.PagamentoDecimal IS NULL OR v.PagamentoDecimal < 0)
                  AND NOT EXISTS (SELECT 1 FROM FinanceiroMigracaoInconsistencias i
                                  WHERE i.ClinicaId = v.ClinicaId AND i.PacienteId = v.Id AND i.Campo = 'Paciente.Pagamento' AND i.Resolvida = 0);

                WITH Valores AS (
                    SELECT p.*,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.Pagamento, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), NCHAR(160), '') END) AS PagamentoDecimal,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.RepasseGlosa, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), NCHAR(160), '') END) AS GlosaDecimal
                    FROM Pacientes p
                )
                INSERT INTO FinanceiroMigracaoInconsistencias
                    (ClinicaId, PacienteId, Campo, ValorOriginal, Motivo, Resolvida, DataCadastro)
                SELECT ClinicaId, Id, 'Paciente.RepasseGlosa', RepasseGlosa,
                       CASE WHEN GlosaDecimal IS NULL THEN 'Valor monetario legado nao pode ser convertido com seguranca.'
                            WHEN GlosaDecimal < 0 THEN 'Glosa legada negativa requer conciliacao.'
                            ELSE 'Glosa legada excede o valor apresentado e requer conciliacao.' END, 0, GETUTCDATE()
                FROM Valores v
                WHERE NULLIF(LTRIM(RTRIM(v.RepasseGlosa)), '') IS NOT NULL
                  AND (v.GlosaDecimal IS NULL OR v.GlosaDecimal < 0 OR (v.PagamentoDecimal IS NOT NULL AND v.GlosaDecimal > v.PagamentoDecimal))
                  AND NOT EXISTS (SELECT 1 FROM FinanceiroMigracaoInconsistencias i
                                  WHERE i.ClinicaId = v.ClinicaId AND i.PacienteId = v.Id AND i.Campo = 'Paciente.RepasseGlosa' AND i.Resolvida = 0);

                WITH Valores AS (
                    SELECT p.*,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.Pagamento, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), NCHAR(160), '') END) AS PagamentoDecimal,
                        TRY_CONVERT(decimal(18,2), CASE WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.RepasseGlosa, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                            THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), '.', ''), ',', '.')
                            ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), NCHAR(160), '') END) AS GlosaDecimal
                    FROM Pacientes p
                )
                INSERT INTO Faturamentos
                    (ClinicaId, AtendimentoCirurgicoId, ConvenioId, NumeroGuia, NumeroLote, Competencia,
                     DataEnvio, DataRetorno, ValorApresentado, ValorGlosado, ValorGlosaRecuperada,
                     ValorReconhecido, Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT v.ClinicaId, a.Id, v.ConvenioId, NULL, NULL,
                       DATEFROMPARTS(YEAR(a.DataProcedimento), MONTH(a.DataProcedimento), 1), NULL,
                       CASE WHEN v.StatusPago = 1 THEN GETUTCDATE() ELSE NULL END,
                       v.PagamentoDecimal, CASE WHEN COALESCE(v.GlosaDecimal, 0) > v.PagamentoDecimal THEN v.PagamentoDecimal ELSE COALESCE(v.GlosaDecimal, 0) END,
                       0, v.PagamentoDecimal - CASE WHEN COALESCE(v.GlosaDecimal, 0) > v.PagamentoDecimal THEN v.PagamentoDecimal ELSE COALESCE(v.GlosaDecimal, 0) END,
                       CASE WHEN v.StatusPago = 1 THEN 'Pago' WHEN COALESCE(v.GlosaDecimal, 0) >= v.PagamentoDecimal THEN 'GlosadoTotal'
                            WHEN COALESCE(v.GlosaDecimal, 0) > 0 THEN 'GlosadoParcial' ELSE 'Aprovado' END,
                       '[LEG-PACIENTE-FINANCEIRO] Migrado de Paciente.Pagamento/RepasseGlosa; revisar conciliacao.',
                       GETUTCDATE(), NULL
                FROM Valores v
                INNER JOIN AtendimentosCirurgicos a ON a.PacienteId = v.Id AND a.ClinicaId = v.ClinicaId
                WHERE v.PagamentoDecimal > 0
                  AND NOT EXISTS (SELECT 1 FROM FaturamentosMedicos fm WHERE fm.PacienteId = v.Id AND fm.ClinicaId = v.ClinicaId)
                  AND NOT EXISTS (SELECT 1 FROM Faturamentos f WHERE f.AtendimentoCirurgicoId = a.Id);

                INSERT INTO FaturamentoItens
                    (ClinicaId, FaturamentoId, AtendimentoProcedimentoId, Codigo, Descricao, Quantidade, PesoPercentual,
                     ValorUnitario, ValorApresentado, ValorGlosado, ValorAprovado, MotivoGlosa, Status, Ordem, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, NULL, NULL, 'Valor convertido do cadastro legado do paciente', 1, 100,
                       f.ValorApresentado, f.ValorApresentado, f.ValorGlosado, f.ValorReconhecido,
                       CASE WHEN f.ValorGlosado > 0 THEN 'Paciente.RepasseGlosa' ELSE NULL END,
                       CASE WHEN f.ValorGlosado >= f.ValorApresentado THEN 'GlosadoTotal' WHEN f.ValorGlosado > 0 THEN 'GlosadoParcial' ELSE 'Aprovado' END,
                       1, f.DataCadastro, NULL
                FROM Faturamentos f
                WHERE f.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%' AND NOT EXISTS (SELECT 1 FROM FaturamentoItens i WHERE i.FaturamentoId = f.Id);

                INSERT INTO Glosas
                    (ClinicaId, FaturamentoId, FaturamentoItemId, CodigoMotivo, DescricaoMotivo, ValorGlosado,
                     DataGlosa, Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, i.Id, NULL, 'Glosa convertida de Paciente.RepasseGlosa', f.ValorGlosado,
                       f.DataCadastro, 'Aberta', '[LEG-PACIENTE-FINANCEIRO] Valor original preservado no paciente.', f.DataCadastro, NULL
                FROM Faturamentos f INNER JOIN FaturamentoItens i ON i.FaturamentoId = f.Id
                WHERE f.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%' AND f.ValorGlosado > 0
                  AND NOT EXISTS (SELECT 1 FROM Glosas g WHERE g.FaturamentoId = f.Id);

                INSERT INTO ContasReceber
                    (ClinicaId, FaturamentoId, ConvenioId, PacienteId, NumeroDocumento, Descricao, Competencia,
                     DataEmissao, DataVencimento, ValorOriginal, ValorAjustado, ValorRecebido, SaldoAberto,
                     Status, Observacao, DataCadastro, DataAtualizacao)
                SELECT f.ClinicaId, f.Id, f.ConvenioId, a.PacienteId, CONCAT('LEG-PAC-', a.PacienteId, '-', f.Id),
                       'Titulo convertido do cadastro legado do paciente', f.Competencia, f.DataCadastro,
                       DATEADD(DAY, 30, f.DataCadastro), f.ValorApresentado, f.ValorReconhecido,
                       CASE WHEN p.StatusPago = 1 THEN f.ValorReconhecido ELSE 0 END,
                       CASE WHEN p.StatusPago = 1 THEN 0 ELSE f.ValorReconhecido END,
                       CASE WHEN p.StatusPago = 1 THEN 'Recebido' WHEN DATEADD(DAY, 30, f.DataCadastro) < GETUTCDATE() THEN 'Vencido' ELSE 'Aberto' END,
                       '[LEG-PACIENTE-FINANCEIRO] Pagamento e status originais preservados em Pacientes.', f.DataCadastro, NULL
                FROM Faturamentos f INNER JOIN AtendimentosCirurgicos a ON a.Id = f.AtendimentoCirurgicoId
                INNER JOIN Pacientes p ON p.Id = a.PacienteId
                WHERE f.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%'
                  AND NOT EXISTS (SELECT 1 FROM ContasReceber c WHERE c.FaturamentoId = f.Id);

                INSERT INTO Recebimentos
                    (ClinicaId, ContaReceberId, DataRecebimento, ValorRecebido, FormaRecebimento,
                     ReferenciaBancaria, DocumentoComprovante, Observacao, UsuarioCadastroId,
                     DataCadastro, Estornado, DataEstorno, UsuarioEstornoId, MotivoEstorno)
                SELECT c.ClinicaId, c.Id, c.DataCadastro, c.ValorRecebido, 'Outro', NULL, NULL,
                       '[LEG-PACIENTE-FINANCEIRO] Recebimento historico criado de StatusPago.', p.UserId,
                       c.DataCadastro, 0, NULL, NULL, NULL
                FROM ContasReceber c INNER JOIN Pacientes p ON p.Id = c.PacienteId
                WHERE c.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%' AND c.ValorRecebido > 0
                  AND NOT EXISTS (SELECT 1 FROM Recebimentos r WHERE r.ContaReceberId = c.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE r FROM Recebimentos r INNER JOIN ContasReceber c ON c.Id = r.ContaReceberId
                    WHERE c.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%';
                DELETE FROM ContasReceber WHERE Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%';
                DELETE g FROM Glosas g INNER JOIN Faturamentos f ON f.Id = g.FaturamentoId
                    WHERE f.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%';
                DELETE i FROM FaturamentoItens i INNER JOIN Faturamentos f ON f.Id = i.FaturamentoId
                    WHERE f.Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%';
                DELETE FROM Faturamentos WHERE Observacao LIKE '[[]LEG-PACIENTE-FINANCEIRO]%';
                """);
            migrationBuilder.DropTable(
                name: "FinanceiroMigracaoInconsistencias");
        }
    }
}
