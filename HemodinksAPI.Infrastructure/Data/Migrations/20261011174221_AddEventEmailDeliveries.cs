using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventEmailDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventEmailDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    SenderEquipeId = table.Column<int>(type: "int", nullable: true),
                    SenderEquipeOperadorId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventEmailDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventEmailDeliveries_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventEmailDeliveries_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventEmailDeliveries_Users_ClinicaId_RecipientUserId",
                        columns: x => new { x.ClinicaId, x.RecipientUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventEmailDeliveries_Users_ClinicaId_SenderUserId",
                        columns: x => new { x.ClinicaId, x.SenderUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "ClinicaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_ClinicaId_EventId_RecipientEmail",
                table: "EventEmailDeliveries",
                columns: new[] { "ClinicaId", "EventId", "RecipientEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_ClinicaId_EventId_RecipientUserId",
                table: "EventEmailDeliveries",
                columns: new[] { "ClinicaId", "EventId", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_ClinicaId_RecipientUserId",
                table: "EventEmailDeliveries",
                columns: new[] { "ClinicaId", "RecipientUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_ClinicaId_SenderUserId",
                table: "EventEmailDeliveries",
                columns: new[] { "ClinicaId", "SenderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_EventId",
                table: "EventEmailDeliveries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventEmailDeliveries_Status_NextAttemptAt_LeaseUntil",
                table: "EventEmailDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventEmailDeliveries");
        }
    }
}
