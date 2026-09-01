using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginAccountProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BloqueadoAte",
                table: "UsuariosGlobais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasLoginFalhas",
                table: "UsuariosGlobais",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaFalhaLoginEm",
                table: "UsuariosGlobais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_BloqueadoAte",
                table: "UsuariosGlobais",
                column: "BloqueadoAte");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsuariosGlobais_BloqueadoAte",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "BloqueadoAte",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "TentativasLoginFalhas",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "UltimaFalhaLoginEm",
                table: "UsuariosGlobais");
        }
    }
}
