using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_DisablePacienteFullTextStoplist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.fulltext_indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Pacientes]'))
                BEGIN
                    ALTER FULLTEXT INDEX ON [dbo].[Pacientes] SET STOPLIST = OFF;
                END;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.fulltext_indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Pacientes]'))
                BEGIN
                    ALTER FULLTEXT INDEX ON [dbo].[Pacientes] SET STOPLIST = SYSTEM;
                END;
                """,
                suppressTransaction: true);
        }
    }
}
