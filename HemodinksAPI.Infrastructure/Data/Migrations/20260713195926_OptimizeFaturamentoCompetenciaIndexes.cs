using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFaturamentoCompetenciaIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_Data",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataCadastro",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "DataCadastro" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_Data",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataCadastro",
                table: "FaturamentosMedicos");
        }
    }
}
