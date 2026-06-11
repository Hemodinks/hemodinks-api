using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConvenios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConvenioId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Convenios",
                columns: table => new
                {
                    IdConvenio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DescricaoConvenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Convenios", x => x.IdConvenio);
                });

            migrationBuilder.InsertData(
                table: "Convenios",
                columns: new[] { "IdConvenio", "DescricaoConvenio" },
                values: new object[,]
                {
                    { 1, "Amil" },
                    { 2, "Bradesco Saúde" },
                    { 3, "Cemig Saúde" },
                    { 4, "Fusex" },
                    { 5, "Geap" },
                    { 6, "Ipsemg" },
                    { 7, "Particular" },
                    { 8, "Sul América" },
                    { 9, "Unimed Uberlândia - Plano  Unimed Intercâmbio" }
                });

            migrationBuilder.Sql("""
                UPDATE p
                SET p.ConvenioId = c.IdConvenio,
                    p.Convenio = c.DescricaoConvenio
                FROM Pacientes p
                INNER JOIN Convenios c
                    ON LTRIM(RTRIM(p.Convenio)) = c.DescricaoConvenio
                WHERE p.ConvenioId IS NULL
                    AND p.Convenio IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ConvenioId",
                table: "Pacientes",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_Convenios_DescricaoConvenio",
                table: "Convenios",
                column: "DescricaoConvenio",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Convenios_ConvenioId",
                table: "Pacientes",
                column: "ConvenioId",
                principalTable: "Convenios",
                principalColumn: "IdConvenio",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Convenios_ConvenioId",
                table: "Pacientes");

            migrationBuilder.DropTable(
                name: "Convenios");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ConvenioId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "ConvenioId",
                table: "Pacientes");
        }
    }
}
