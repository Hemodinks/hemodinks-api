using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AuditoriaDetalhe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AuditarAtendimentos",
                table: "ConfiguracoesSistema",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Detalhe",
                table: "AuditoriasClinica",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PacienteId",
                table: "AuditoriasClinica",
                type: "int",
                nullable: true);

            // Trilha anterior: em acoes de paciente o RecursoId ja e o id do paciente. EXEC adia a compilacao
            // (no script idempotente a coluna ainda nao existe quando o lote e compilado). Nao altera outros dados.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE dbo.AuditoriasClinica SET PacienteId = TRY_CONVERT(int, RecursoId) " +
                "WHERE Recurso = N''paciente'' AND RecursoId IS NOT NULL AND PacienteId IS NULL;');");

            migrationBuilder.UpdateData(
                table: "ConfiguracoesSistema",
                keyColumn: "Id",
                keyValue: 1,
                column: "AuditarAtendimentos",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasClinica_ClinicaId_PacienteId_DataUtc",
                table: "AuditoriasClinica",
                columns: new[] { "ClinicaId", "PacienteId", "DataUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditoriasClinica_ClinicaId_PacienteId_DataUtc",
                table: "AuditoriasClinica");

            migrationBuilder.DropColumn(
                name: "AuditarAtendimentos",
                table: "ConfiguracoesSistema");

            migrationBuilder.DropColumn(
                name: "Detalhe",
                table: "AuditoriasClinica");

            migrationBuilder.DropColumn(
                name: "PacienteId",
                table: "AuditoriasClinica");
        }
    }
}
