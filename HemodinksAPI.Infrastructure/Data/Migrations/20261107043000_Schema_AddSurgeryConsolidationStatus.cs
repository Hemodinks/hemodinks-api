using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AddSurgeryConsolidationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusCirurgia",
                table: "Pacientes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NaoConsolidada");

            // Backfill: cirurgias com data da consolidação já preenchida passam a ser consolidadas.
            // EXEC adia a compilação até a coluna existir (scripts idempotentes compilam o lote inteiro).
            migrationBuilder.Sql("""
                EXEC(N'UPDATE [Pacientes] SET [StatusCirurgia] = N''Consolidada'' WHERE [DataAtendimento] IS NOT NULL;');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pacientes_StatusCirurgia",
                table: "Pacientes",
                sql: "[StatusCirurgia] IN (N'NaoConsolidada', N'Consolidada')");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_StatusCirurgia",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "StatusCirurgia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O status é derivado de [DataAtendimento], que permanece intacta: remover a coluna
            // não perde informação e um novo Up() recalcula o status pelo backfill.
            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_StatusCirurgia",
                table: "Pacientes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pacientes_StatusCirurgia",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "StatusCirurgia",
                table: "Pacientes");
        }
    }
}
