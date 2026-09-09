using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryAccessCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecurityVersion",
                table: "UsuariosGlobais",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "TemporaryPasswordRecovery",
                table: "UsuariosGlobais",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityVersion",
                table: "EquipeLoginDesafios",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityVersion",
                table: "AuthenticationSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "TemporaryAccessCredentials",
                columns: table => new
                {
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryAccessCredentials", x => x.UsuarioGlobalId);
                    table.ForeignKey(
                        name: "FK_TemporaryAccessCredentials_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAccessCredentials_ClinicaId_UserId",
                table: "TemporaryAccessCredentials",
                columns: new[] { "ClinicaId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemporaryAccessCredentials");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "TemporaryPasswordRecovery",
                table: "UsuariosGlobais");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "EquipeLoginDesafios");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "AuthenticationSessions");
        }
    }
}
