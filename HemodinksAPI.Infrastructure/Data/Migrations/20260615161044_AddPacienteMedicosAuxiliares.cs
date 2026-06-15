using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteMedicosAuxiliares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MedicoAuxiliar1",
                table: "Pacientes",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MedicoAuxiliar1UserId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicoAuxiliar2",
                table: "Pacientes",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MedicoAuxiliar2UserId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoAuxiliar1UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar1UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoAuxiliar2UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar2UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar1UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar1UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar2UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar2UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar2UserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_MedicoAuxiliar2UserId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "MedicoAuxiliar1",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "MedicoAuxiliar2",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "MedicoAuxiliar2UserId",
                table: "Pacientes");
        }
    }
}
