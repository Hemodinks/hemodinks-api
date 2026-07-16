using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturamentoMedicoCompetencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompetenciaFinal",
                table: "FaturamentosMedicos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompetenciaInicio",
                table: "FaturamentosMedicos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE f
                SET
                    [CompetenciaInicio] = DATEFROMPARTS(YEAR(p.[Data]), MONTH(p.[Data]), 1),
                    [CompetenciaFinal] = EOMONTH(p.[Data])
                FROM [FaturamentosMedicos] f
                INNER JOIN [Pacientes] p
                    ON p.[Id] = f.[PacienteId]
                WHERE p.[Data] IS NOT NULL
                    AND f.[CompetenciaInicio] IS NULL
                    AND f.[CompetenciaFinal] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_CompetenciaInicio_CompetenciaFinal",
                table: "FaturamentosMedicos",
                columns: new[] { "CompetenciaInicio", "CompetenciaFinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_CompetenciaInicio_CompetenciaFinal",
                table: "FaturamentosMedicos");

            migrationBuilder.DropColumn(
                name: "CompetenciaFinal",
                table: "FaturamentosMedicos");

            migrationBuilder.DropColumn(
                name: "CompetenciaInicio",
                table: "FaturamentosMedicos");
        }
    }
}
