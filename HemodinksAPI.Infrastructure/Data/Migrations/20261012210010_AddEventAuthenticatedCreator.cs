using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAuthenticatedCreator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClinicaId_CreatedByUserId",
                table: "Events",
                columns: new[] { "ClinicaId", "CreatedByUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Users_ClinicaId_CreatedByUserId",
                table: "Events",
                columns: new[] { "ClinicaId", "CreatedByUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Users_ClinicaId_CreatedByUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ClinicaId_CreatedByUserId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Events");
        }
    }
}
