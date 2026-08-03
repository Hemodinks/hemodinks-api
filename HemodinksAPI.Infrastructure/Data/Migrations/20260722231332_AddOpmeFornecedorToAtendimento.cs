using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpmeFornecedorToAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpmeFornecedorId",
                table: "AtendimentosCirurgicos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_OpmeFornecedorId",
                table: "AtendimentosCirurgicos",
                column: "OpmeFornecedorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_OPME_OpmeFornecedorId",
                table: "AtendimentosCirurgicos",
                column: "OpmeFornecedorId",
                principalTable: "OPME",
                principalColumn: "IdFornecedor",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_OPME_OpmeFornecedorId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_OpmeFornecedorId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropColumn(
                name: "OpmeFornecedorId",
                table: "AtendimentosCirurgicos");
        }
    }
}
