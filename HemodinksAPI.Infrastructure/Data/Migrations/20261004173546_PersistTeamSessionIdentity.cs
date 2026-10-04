using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistTeamSessionIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EquipeId",
                table: "AuthenticationSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipeOperadorId",
                table: "AuthenticationSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipeVersaoSessao",
                table: "AuthenticationSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IdentificacaoConfiavel",
                table: "AuthenticationSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OperadorVersaoSessao",
                table: "AuthenticationSessions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquipeId",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "EquipeOperadorId",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "EquipeVersaoSessao",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "IdentificacaoConfiavel",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "OperadorVersaoSessao",
                table: "AuthenticationSessions");
        }
    }
}
