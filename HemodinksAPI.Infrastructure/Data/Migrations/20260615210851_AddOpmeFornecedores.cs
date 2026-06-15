using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpmeFornecedores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpmeFornecedor",
                table: "Pacientes",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpmeFornecedorId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OPME",
                columns: table => new
                {
                    IdFornecedor = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fornecedor = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OPME", x => x.IdFornecedor);
                });

            migrationBuilder.InsertData(
                table: "OPME",
                columns: new[] { "IdFornecedor", "Fornecedor" },
                values: new object[,]
                {
                    { 1, "Promedom" },
                    { 2, "AVL" },
                    { 3, "GE" },
                    { 4, "Spyner" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_OpmeFornecedorId",
                table: "Pacientes",
                column: "OpmeFornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_OPME_Fornecedor",
                table: "OPME",
                column: "Fornecedor",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_OPME_OpmeFornecedorId",
                table: "Pacientes",
                column: "OpmeFornecedorId",
                principalTable: "OPME",
                principalColumn: "IdFornecedor",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_OPME_OpmeFornecedorId",
                table: "Pacientes");

            migrationBuilder.DropTable(
                name: "OPME");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_OpmeFornecedorId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "OpmeFornecedor",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "OpmeFornecedorId",
                table: "Pacientes");
        }
    }
}
