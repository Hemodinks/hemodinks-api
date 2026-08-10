using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsAndOperatorIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Equipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UsuarioLoginId = table.Column<int>(type: "int", nullable: false),
                    ModoIdentificacao = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    VersaoSessao = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipes_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipes_Users_UsuarioLoginId",
                        column: x => x.UsuarioLoginId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipeLoginDesafios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeLoginDesafios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeLoginDesafios_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeLoginDesafios_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipeMembros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeMembros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipeOperadores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrecisaTrocarPin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    TentativasFalhas = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    BloqueadoAte = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VersaoSessao = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataUltimaTroca = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeOperadores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Perfis",
                columns: new[] { "Id", "Nome" },
                values: new object[] { 6, "Equipe" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_ClinicaId_EquipeId_ExpiraEm_UtilizadoEm",
                table: "EquipeLoginDesafios",
                columns: new[] { "ClinicaId", "EquipeId", "ExpiraEm", "UtilizadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_ClinicaId_TokenHash",
                table: "EquipeLoginDesafios",
                columns: new[] { "ClinicaId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_EquipeId",
                table: "EquipeLoginDesafios",
                column: "EquipeId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_ClinicaId_UserId_Ativo",
                table: "EquipeMembros",
                columns: new[] { "ClinicaId", "UserId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_EquipeId_UserId",
                table: "EquipeMembros",
                columns: new[] { "EquipeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_UserId",
                table: "EquipeMembros",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_ClinicaId_Ativo",
                table: "EquipeOperadores",
                columns: new[] { "ClinicaId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_EquipeId_UserId",
                table: "EquipeOperadores",
                columns: new[] { "EquipeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_UserId",
                table: "EquipeOperadores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_ClinicaId_Nome",
                table: "Equipes",
                columns: new[] { "ClinicaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_UsuarioLoginId",
                table: "Equipes",
                column: "UsuarioLoginId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipeLoginDesafios");

            migrationBuilder.DropTable(
                name: "EquipeMembros");

            migrationBuilder.DropTable(
                name: "EquipeOperadores");

            migrationBuilder.DropTable(
                name: "Equipes");

            migrationBuilder.DeleteData(
                table: "Perfis",
                keyColumn: "Id",
                keyValue: 6);
        }
    }
}
