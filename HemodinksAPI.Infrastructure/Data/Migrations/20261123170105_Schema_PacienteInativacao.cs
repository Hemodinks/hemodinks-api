using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_PacienteInativacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                table: "Pacientes",
                type: "bit",
                nullable: false,
                // #369: todos os pacientes existentes continuam ativos.
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InativadoEm",
                table: "Pacientes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InativadoPorUserId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_Ativo",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "Ativo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_Ativo",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "Ativo",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "InativadoEm",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "InativadoPorUserId",
                table: "Pacientes");
        }
    }
}
