using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllDayEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AllDayEndDate",
                table: "Events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AllDayStartDate",
                table: "Events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                table: "Events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Events",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Events_AllDayPeriod",
                table: "Events",
                sql: "([IsAllDay] = 0 AND [AllDayStartDate] IS NULL AND [AllDayEndDate] IS NULL AND [TimeZoneId] IS NULL) OR ([IsAllDay] = 1 AND [AllDayStartDate] IS NOT NULL AND [AllDayEndDate] IS NOT NULL AND [AllDayEndDate] >= [AllDayStartDate] AND [TimeZoneId] IS NOT NULL AND [TimeZoneId] <> '' AND [End] > [Start])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Events_AllDayPeriod",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AllDayEndDate",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AllDayStartDate",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsAllDay",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Events");
        }
    }
}
