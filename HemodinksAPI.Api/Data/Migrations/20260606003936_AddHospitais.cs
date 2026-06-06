using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HospitalId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Hospitais",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hospitais", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Hospitais",
                columns: new[] { "Id", "Nome" },
                values: new object[,]
                {
                    { 1, "Santa Clara - Mater Dei" },
                    { 2, "Santa Genoveva - Mater Dei" },
                    { 3, "UMC - Complexo Hospitalar" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_HospitalId",
                table: "Pacientes",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitais_Nome",
                table: "Hospitais",
                column: "Nome",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Hospitais_HospitalId",
                table: "Pacientes",
                column: "HospitalId",
                principalTable: "Hospitais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Hospitais_HospitalId",
                table: "Pacientes");

            migrationBuilder.DropTable(
                name: "Hospitais");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_HospitalId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "HospitalId",
                table: "Pacientes");
        }
    }
}
