using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowSharedTeamMemberEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users",
                columns: new[] { "ClinicaId", "Email" },
                unique: true,
                filter: "[PerfilId] <> 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users",
                columns: new[] { "ClinicaId", "Email" },
                unique: true);
        }
    }
}
