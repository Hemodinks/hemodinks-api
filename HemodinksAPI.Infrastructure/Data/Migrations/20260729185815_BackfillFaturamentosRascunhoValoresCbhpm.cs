using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillFaturamentosRascunhoValoresCbhpm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE item
                SET
                    item.[ValorUnitario] = totais.[ValorApresentado],
                    item.[ValorApresentado] = totais.[ValorApresentado],
                    item.[ValorAprovado] = totais.[ValorApresentado],
                    item.[DataAtualizacao] = GETUTCDATE()
                FROM [FaturamentoItens] AS item
                INNER JOIN [Faturamentos] AS faturamento
                    ON faturamento.[Id] = item.[FaturamentoId]
                CROSS APPLY
                (
                    SELECT
                        SUM(ROUND(
                            procedimento.[Quantidade]
                            * procedimento.[PesoPercentual]
                            / 100.0
                            * COALESCE(
                                procedimento.[ValorNegociado],
                                procedimento.[ValorReferencia],
                                0
                            ),
                            2
                        )) AS [ValorApresentado]
                    FROM [AtendimentoProcedimentos] AS procedimento
                    WHERE
                        procedimento.[AtendimentoCirurgicoId] =
                            faturamento.[AtendimentoCirurgicoId]
                ) AS totais
                WHERE
                    faturamento.[Status] = 'Rascunho'
                    AND faturamento.[ValorApresentado] = 0
                    AND faturamento.[ValorGlosado] = 0
                    AND faturamento.[ValorGlosaRecuperada] = 0
                    AND faturamento.[ValorReconhecido] = 0
                    AND item.[AtendimentoProcedimentoId] IS NULL
                    AND item.[Descricao] = 'Valor consolidado do faturamento legado'
                    AND item.[ValorUnitario] = 0
                    AND item.[ValorApresentado] = 0
                    AND item.[ValorGlosado] = 0
                    AND item.[ValorAprovado] = 0
                    AND totais.[ValorApresentado] > 0
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM [Glosas] AS glosa
                        WHERE glosa.[FaturamentoId] = faturamento.[Id]
                    )
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM [ContasReceber] AS conta
                        WHERE conta.[FaturamentoId] = faturamento.[Id]
                    );

                UPDATE faturamento
                SET
                    faturamento.[ValorApresentado] = totais.[ValorApresentado],
                    faturamento.[ValorReconhecido] = totais.[ValorApresentado],
                    faturamento.[DataAtualizacao] = GETUTCDATE()
                FROM [Faturamentos] AS faturamento
                CROSS APPLY
                (
                    SELECT SUM(item.[ValorApresentado]) AS [ValorApresentado]
                    FROM [FaturamentoItens] AS item
                    WHERE
                        item.[FaturamentoId] = faturamento.[Id]
                        AND item.[Status] <> 'Cancelado'
                ) AS totais
                WHERE
                    faturamento.[Status] = 'Rascunho'
                    AND faturamento.[ValorApresentado] = 0
                    AND faturamento.[ValorGlosado] = 0
                    AND faturamento.[ValorGlosaRecuperada] = 0
                    AND faturamento.[ValorReconhecido] = 0
                    AND totais.[ValorApresentado] > 0
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM [Glosas] AS glosa
                        WHERE glosa.[FaturamentoId] = faturamento.[Id]
                    )
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM [ContasReceber] AS conta
                        WHERE conta.[FaturamentoId] = faturamento.[Id]
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: existing financial values cannot be distinguished
            // safely from values populated by this data backfill.
        }
    }
}
