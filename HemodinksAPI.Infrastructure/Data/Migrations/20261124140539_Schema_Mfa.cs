using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_Mfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaHabilitado",
                table: "UsuariosGlobais",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaHabilitadoEm",
                table: "UsuariosGlobais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSegredoPendenteProtegido",
                table: "UsuariosGlobais",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSegredoProtegido",
                table: "UsuariosGlobais",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MfaUltimoPassoUsado",
                table: "UsuariosGlobais",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfaCodigosRecuperacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    CodigoHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UsadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaCodigosRecuperacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaCodigosRecuperacao_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfaLoginDesafios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SecurityVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    UtilizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaLoginDesafios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaLoginDesafios_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaLoginDesafios_Users_ClinicaId_UserId",
                        columns: x => new { x.ClinicaId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfaLoginDesafios_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaCodigosRecuperacao_UsuarioGlobalId_CodigoHash",
                table: "MfaCodigosRecuperacao",
                columns: new[] { "UsuarioGlobalId", "CodigoHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginDesafios_ClinicaId_TokenHash",
                table: "MfaLoginDesafios",
                columns: new[] { "ClinicaId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginDesafios_ClinicaId_UserId",
                table: "MfaLoginDesafios",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginDesafios_ExpiraEm",
                table: "MfaLoginDesafios",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginDesafios_UsuarioGlobalId",
                table: "MfaLoginDesafios",
                column: "UsuarioGlobalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaCodigosRecuperacao");

            migrationBuilder.DropTable(
                name: "MfaLoginDesafios");

            migrationBuilder.DropColumn(
                name: "MfaHabilitado",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "MfaHabilitadoEm",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "MfaSegredoPendenteProtegido",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "MfaSegredoProtegido",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "MfaUltimoPassoUsado",
                table: "UsuariosGlobais");
        }
    }
}
