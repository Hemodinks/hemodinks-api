using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteObservacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Observacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    AutorUserId = table.Column<int>(type: "int", nullable: false),
                    DestinatarioUserId = table.Column<int>(type: "int", nullable: false),
                    ObservacaoPaiId = table.Column<int>(type: "int", nullable: true),
                    Texto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataLeitura = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MedicoUserId = table.Column<int>(type: "int", nullable: true),
                    Medico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar1UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar1 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar2UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observacoes_Observacoes_ObservacaoPaiId",
                        column: x => x.ObservacaoPaiId,
                        principalTable: "Observacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observacoes_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Observacoes_Users_AutorUserId",
                        column: x => x.AutorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observacoes_Users_DestinatarioUserId",
                        column: x => x.DestinatarioUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_AutorUserId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "AutorUserId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_DestinatarioUserId_DataLeitura_DataCadastro",
                table: "Observacoes",
                columns: new[] { "DestinatarioUserId", "DataLeitura", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_ObservacaoPaiId",
                table: "Observacoes",
                column: "ObservacaoPaiId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_PacienteId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "PacienteId", "DataCadastro" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Observacoes");
        }
    }
}
