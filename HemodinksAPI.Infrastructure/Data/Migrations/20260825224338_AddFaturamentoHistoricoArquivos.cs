using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturamentoHistoricoArquivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaturamentoHistoricoArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Ano = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentoHistoricoArquivos", x => x.Id);
                    table.CheckConstraint("CK_FaturamentoHistoricoArquivos_Ano", "[Ano] BETWEEN 1900 AND 2100");
                    table.CheckConstraint("CK_FaturamentoHistoricoArquivos_Mes", "[Mes] BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_FaturamentoHistoricoArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoHistoricoArquivos_ClinicaId_Ano_Mes_DataUpload",
                table: "FaturamentoHistoricoArquivos",
                columns: new[] { "ClinicaId", "Ano", "Mes", "DataUpload" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaturamentoHistoricoArquivos");
        }
    }
}
