using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteMedicoUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MedicoUserId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE p
                SET MedicoUserId = u.Id
                FROM [Pacientes] p
                INNER JOIN [Users] u ON u.[Nome] = p.[Medico] AND u.[PerfilId] = 2
                WHERE p.[Medico] IS NOT NULL AND p.[MedicoUserId] IS NULL
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoUserId",
                table: "Pacientes",
                column: "MedicoUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoUserId",
                table: "Pacientes",
                column: "MedicoUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoUserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_MedicoUserId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "MedicoUserId",
                table: "Pacientes");
        }
    }
}
