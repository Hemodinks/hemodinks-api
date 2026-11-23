using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_ArquivoExclusaoLogica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExcluidoEm",
                table: "PacienteArquivos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExcluidoPorUserId",
                table: "PacienteArquivos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExcluidoEm",
                table: "AtendimentoArquivos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExcluidoPorUserId",
                table: "AtendimentoArquivos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcluidoEm",
                table: "PacienteArquivos");

            migrationBuilder.DropColumn(
                name: "ExcluidoPorUserId",
                table: "PacienteArquivos");

            migrationBuilder.DropColumn(
                name: "ExcluidoEm",
                table: "AtendimentoArquivos");

            migrationBuilder.DropColumn(
                name: "ExcluidoPorUserId",
                table: "AtendimentoArquivos");
        }
    }
}
