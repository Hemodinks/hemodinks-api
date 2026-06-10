using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixPatientFileUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[PacienteArquivos]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [PacienteArquivos]
                    SET [Url] = REPLACE([Url], '.blob.core.windows.net/profile-photos/pacientes/', '.blob.core.windows.net/patient-files/pacientes/')
                    WHERE [Url] LIKE 'https://%.blob.core.windows.net/profile-photos/pacientes/%';
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[PacienteArquivos]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [PacienteArquivos]
                    SET [Url] = REPLACE([Url], '.blob.core.windows.net/patient-files/pacientes/', '.blob.core.windows.net/profile-photos/pacientes/')
                    WHERE [Url] LIKE 'https://%.blob.core.windows.net/patient-files/pacientes/%';
                END
                """);
        }
    }
}
