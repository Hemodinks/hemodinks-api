using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AddUniqueClinicaCnpj : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT [Cnpj]
                    FROM [Clinicas]
                    WHERE [Cnpj] IS NOT NULL
                    GROUP BY [Cnpj]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Existem clinicas com CNPJ duplicado. Corrija os registros antes de aplicar esta migration.', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Clinicas_Cnpj",
                table: "Clinicas",
                column: "Cnpj",
                unique: true,
                filter: "[Cnpj] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clinicas_Cnpj",
                table: "Clinicas");
        }
    }
}
