using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_RefreshTokenReuseDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousRefreshTokenHash",
                table: "AuthenticationSessions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshRotatedAt",
                table: "AuthenticationSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_PreviousRefreshTokenHash",
                table: "AuthenticationSessions",
                column: "PreviousRefreshTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthenticationSessions_PreviousRefreshTokenHash",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "PreviousRefreshTokenHash",
                table: "AuthenticationSessions");

            migrationBuilder.DropColumn(
                name: "RefreshRotatedAt",
                table: "AuthenticationSessions");
        }
    }
}
