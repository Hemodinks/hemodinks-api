using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AtendimentoCancelamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CanceladoEm",
                table: "AtendimentosCirurgicos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanceladoPorUserId",
                table: "AtendimentosCirurgicos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCancelamento",
                table: "AtendimentosCirurgicos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanceladoEm",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropColumn(
                name: "CanceladoPorUserId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropColumn(
                name: "MotivoCancelamento",
                table: "AtendimentosCirurgicos");
        }
    }
}
