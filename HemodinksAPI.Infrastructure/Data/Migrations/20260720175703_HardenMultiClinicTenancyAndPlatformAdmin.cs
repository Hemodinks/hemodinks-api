using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenMultiClinicTenancyAndPlatformAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRequests_Operation_Scope_IdempotencyKey",
                table: "IdempotencyRequests");

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "IdempotencyRequests",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [IdempotencyRequests]
                SET [ClinicaId] = COALESCE(
                    TRY_CONVERT(int, CASE
                        WHEN [Scope] LIKE 'clinic:%:%'
                        THEN SUBSTRING([Scope], 8, CHARINDEX(':', [Scope], 8) - 8)
                    END),
                    1);
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "IdempotencyRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinaturaStatus",
                table: "Clinicas",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Trial");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssinaturaValidaAte",
                table: "Clinicas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LimiteUsuarios",
                table: "Clinicas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plano",
                table: "Clinicas",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Trial");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialAte",
                table: "Clinicas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Clinicas",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AssinaturaStatus", "AssinaturaValidaAte", "LimiteUsuarios", "Plano", "TrialAte" },
                values: new object[] { "Trial", null, null, "Trial", null });

            migrationBuilder.Sql(
                "UPDATE [Clinicas] SET [TrialAte] = DATEADD(day, 14, SYSUTCDATETIME()) WHERE [TrialAte] IS NULL;");

            migrationBuilder.InsertData(
                table: "Perfis",
                columns: new[] { "Id", "Nome" },
                values: new object[] { 5, "SuperAdministrador" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRequests_ClinicaId_Operation_Scope_IdempotencyKey",
                table: "IdempotencyRequests",
                columns: new[] { "ClinicaId", "Operation", "Scope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyRequests_Clinicas_ClinicaId",
                table: "IdempotencyRequests",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyRequests_Clinicas_ClinicaId",
                table: "IdempotencyRequests");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRequests_ClinicaId_Operation_Scope_IdempotencyKey",
                table: "IdempotencyRequests");

            migrationBuilder.Sql(
                "UPDATE [Users] SET [PerfilId] = 1 WHERE [PerfilId] = 5;");

            migrationBuilder.DeleteData(
                table: "Perfis",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "IdempotencyRequests");

            migrationBuilder.DropColumn(
                name: "AssinaturaStatus",
                table: "Clinicas");

            migrationBuilder.DropColumn(
                name: "AssinaturaValidaAte",
                table: "Clinicas");

            migrationBuilder.DropColumn(
                name: "LimiteUsuarios",
                table: "Clinicas");

            migrationBuilder.DropColumn(
                name: "Plano",
                table: "Clinicas");

            migrationBuilder.DropColumn(
                name: "TrialAte",
                table: "Clinicas");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRequests_Operation_Scope_IdempotencyKey",
                table: "IdempotencyRequests",
                columns: new[] { "Operation", "Scope", "IdempotencyKey" },
                unique: true);
        }
    }
}
