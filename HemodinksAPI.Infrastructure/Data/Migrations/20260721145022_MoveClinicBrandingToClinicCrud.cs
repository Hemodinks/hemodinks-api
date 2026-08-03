using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveClinicBrandingToClinicCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FotoClinica",
                table: "Clinicas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE clinica
                SET
                    clinica.Nome = CASE
                        WHEN configuracao.NomeEmpresa IS NOT NULL
                          AND LTRIM(RTRIM(configuracao.NomeEmpresa)) <> ''
                        THEN LTRIM(RTRIM(configuracao.NomeEmpresa))
                        ELSE clinica.Nome
                    END,
                    clinica.FotoClinica = CASE
                        WHEN configuracao.FotoEmpresa IS NOT NULL
                          AND LTRIM(RTRIM(configuracao.FotoEmpresa)) <> ''
                        THEN configuracao.FotoEmpresa
                        ELSE clinica.FotoClinica
                    END
                FROM Clinicas clinica
                INNER JOIN ConfiguracoesSistema configuracao
                    ON configuracao.ClinicaId = clinica.Id;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoClinica",
                table: "Clinicas");
        }
    }
}
