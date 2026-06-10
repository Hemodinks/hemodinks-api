using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCbhpmGeral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CBHPMGeral",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Procedimento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Porte = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustoOperacional = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    Capitulo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Grupo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PaginaPdf = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CBHPMGeral", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CBHPMGeral_Codigo",
                table: "CBHPMGeral",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CBHPMGeral_Porte",
                table: "CBHPMGeral",
                column: "Porte");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CBHPMGeral");
        }
    }
}
