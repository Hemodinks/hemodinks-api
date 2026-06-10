using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLicencas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Licencas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Plano = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataInicioTrial = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataFimTrial = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataFimLicenca = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FeaturesLiberadas = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licencas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licencas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_UserId",
                table: "Licencas",
                column: "UserId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO Licencas (
                    UserId,
                    Plano,
                    Status,
                    DataInicioTrial,
                    DataFimTrial,
                    DataCadastro
                )
                SELECT
                    Id,
                    'Trial',
                    'Ativa',
                    GETUTCDATE(),
                    DATEADD(day, 14, GETUTCDATE()),
                    GETUTCDATE()
                FROM Users
                WHERE PerfilId = 2
                    AND NOT EXISTS (
                        SELECT 1
                        FROM Licencas
                        WHERE Licencas.UserId = Users.Id
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Licencas");
        }
    }
}
