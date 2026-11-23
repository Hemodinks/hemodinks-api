using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AuditoriaClinica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditoriasClinica",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    EquipeOperadorId = table.Column<int>(type: "int", nullable: true),
                    Acao = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Recurso = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RecursoId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Sucesso = table.Column<bool>(type: "bit", nullable: false),
                    IpTruncado = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriasClinica", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditoriasClinica_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasClinica_ClinicaId_DataUtc",
                table: "AuditoriasClinica",
                columns: new[] { "ClinicaId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasClinica_ClinicaId_Recurso_RecursoId_DataUtc",
                table: "AuditoriasClinica",
                columns: new[] { "ClinicaId", "Recurso", "RecursoId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasClinica_DataUtc",
                table: "AuditoriasClinica",
                column: "DataUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriasClinica");
        }
    }
}
