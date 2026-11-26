using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_RefreshTokenFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticationSessionRefreshHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RotatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSessionRefreshHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthenticationSessionRefreshHistory_AuthenticationSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AuthenticationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionRefreshHistory_Hash",
                table: "AuthenticationSessionRefreshHistory",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionRefreshHistory_RotatedAt",
                table: "AuthenticationSessionRefreshHistory",
                column: "RotatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionRefreshHistory_SessionId",
                table: "AuthenticationSessionRefreshHistory",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSessionRefreshHistory");
        }
    }
}
