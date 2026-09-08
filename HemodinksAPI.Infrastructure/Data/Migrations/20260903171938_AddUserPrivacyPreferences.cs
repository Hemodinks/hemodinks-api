using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPrivacyPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPrivacyPreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    DocumentVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreferencesEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AnalyticsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPrivacyPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPrivacyPreferences_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPrivacyPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPrivacyPreferences_ClinicaId_UserId",
                table: "UserPrivacyPreferences",
                columns: new[] { "ClinicaId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPrivacyPreferences_UserId",
                table: "UserPrivacyPreferences",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPrivacyPreferences");
        }
    }
}
