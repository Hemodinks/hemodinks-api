using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturamentoMedicoDataPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataPagamento",
                table: "FaturamentosMedicos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataPagamento",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "DataPagamento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataPagamento",
                table: "FaturamentosMedicos");

            migrationBuilder.DropColumn(
                name: "DataPagamento",
                table: "FaturamentosMedicos");
        }
    }
}
