using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAtendimentoProcedimentoValoresCbhpm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE procedimento
                SET
                    procedimento.[ValorReferencia] =
                        COALESCE(procedimento.[ValorReferencia], referencia.[ValorReferencia]),
                    procedimento.[ValorNegociado] =
                        COALESCE(procedimento.[ValorNegociado], preco.[ValorNegociado]),
                    procedimento.[CbhpmPorte] =
                        COALESCE(procedimento.[CbhpmPorte], referencia.[Porte])
                FROM [AtendimentoProcedimentos] AS procedimento
                INNER JOIN [AtendimentosCirurgicos] AS atendimento
                    ON atendimento.[Id] = procedimento.[AtendimentoCirurgicoId]
                CROSS APPLY
                (
                    SELECT TOP (1)
                        cbhpm.[Id],
                        cbhpm.[Porte],
                        cbhpm.[ValorReferencia]
                    FROM [CBHPMGeral] AS cbhpm
                    WHERE
                        REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(cbhpm.[Codigo])),
                            '.', ''), '-', ''), '/', ''), ' ', '')
                        =
                        REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(procedimento.[CbhpmCodigo])),
                            '.', ''), '-', ''), '/', ''), ' ', '')
                    ORDER BY
                        CASE WHEN cbhpm.[Codigo] = procedimento.[CbhpmCodigo] THEN 0 ELSE 1 END,
                        cbhpm.[Id]
                ) AS referencia
                OUTER APPLY
                (
                    SELECT TOP (1)
                        tabela.[ValorNegociado]
                    FROM [ConvenioProcedimentoPrecos] AS tabela
                    WHERE
                        tabela.[ClinicaId] = procedimento.[ClinicaId]
                        AND tabela.[ConvenioId] = atendimento.[ConvenioId]
                        AND tabela.[Ativo] = 1
                        AND tabela.[VigenciaInicio] <= atendimento.[DataProcedimento]
                        AND (
                            tabela.[VigenciaFinal] IS NULL
                            OR tabela.[VigenciaFinal] >= atendimento.[DataProcedimento]
                        )
                        AND
                            REPLACE(REPLACE(REPLACE(REPLACE(
                                LTRIM(RTRIM(tabela.[CbhpmCodigo])),
                                '.', ''), '-', ''), '/', ''), ' ', '')
                            =
                            REPLACE(REPLACE(REPLACE(REPLACE(
                                LTRIM(RTRIM(procedimento.[CbhpmCodigo])),
                                '.', ''), '-', ''), '/', ''), ' ', '')
                    ORDER BY tabela.[VigenciaInicio] DESC, tabela.[Id] DESC
                ) AS preco
                WHERE
                    procedimento.[CbhpmCodigo] IS NOT NULL
                    AND (
                        procedimento.[ValorReferencia] IS NULL
                        OR procedimento.[ValorNegociado] IS NULL
                        OR procedimento.[CbhpmPorte] IS NULL
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: existing values cannot be distinguished safely
            // from values populated by this data backfill.
        }
    }
}
