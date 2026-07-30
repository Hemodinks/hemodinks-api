using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAtendimentoObservacaoArquivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Observacao",
                table: "AtendimentosCirurgicos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AtendimentoArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentoArquivos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtendimentoArquivos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AtendimentoArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoArquivos_AtendimentoCirurgicoId",
                table: "AtendimentoArquivos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoArquivos_ClinicaId_AtendimentoCirurgicoId",
                table: "AtendimentoArquivos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtendimentoArquivos");

            migrationBuilder.DropColumn(
                name: "Observacao",
                table: "AtendimentosCirurgicos");
        }
    }
}
