using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteProcedimentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PacienteProcedimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CbhpmPorte = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Procedimento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValorReferencia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Ordem = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PacienteProcedimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PacienteProcedimentos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_CbhpmCodigo",
                table: "PacienteProcedimentos",
                column: "CbhpmCodigo");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_PacienteId",
                table: "PacienteProcedimentos",
                column: "PacienteId");

            migrationBuilder.Sql("""
                INSERT INTO [PacienteProcedimentos] ([PacienteId], [CbhpmCodigo], [CbhpmPorte], [Procedimento], [ValorReferencia], [Ordem])
                SELECT [p].[Id], [p].[CbhpmCodigo], [p].[CbhpmPorte], [p].[Procedimento], [c].[ValorReferencia], 1
                FROM [Pacientes] AS [p]
                LEFT JOIN [CBHPMGeral] AS [c] ON [c].[Codigo] = [p].[CbhpmCodigo]
                WHERE NULLIF(LTRIM(RTRIM([p].[Procedimento])), '') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PacienteProcedimentos");
        }
    }
}
