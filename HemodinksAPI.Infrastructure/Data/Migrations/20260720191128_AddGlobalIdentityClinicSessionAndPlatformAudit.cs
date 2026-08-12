using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalIdentityClinicSessionAndPlatformAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsuariosGlobais",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Senha = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosGlobais", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditoriasPlataforma",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Acao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Recurso = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntidadeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetalhesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Sucesso = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriasPlataforma", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditoriasPlataforma_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditoriasPlataforma_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuariosClinicas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PerfilId = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ClinicaPadrao = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosClinicas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Perfis_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                ;WITH UsuariosOrdenados AS
                (
                    SELECT
                        u.Id,
                        u.Nome,
                        LOWER(LTRIM(RTRIM(u.Email))) AS EmailNormalizado,
                        u.Senha,
                        u.Ativo,
                        u.DataCadastro,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY LOWER(LTRIM(RTRIM(u.Email)))
                            ORDER BY CASE WHEN u.ClinicaId = 1 THEN 0 ELSE 1 END, u.Id
                        ) AS Ordem
                    FROM Users u
                )
                INSERT INTO UsuariosGlobais (Nome, Email, Senha, Ativo, DataCadastro, DataAtualizacao)
                SELECT
                    escolhido.Nome,
                    escolhido.EmailNormalizado,
                    escolhido.Senha,
                    CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM Users ativo
                        WHERE LOWER(LTRIM(RTRIM(ativo.Email))) = escolhido.EmailNormalizado
                          AND ativo.Ativo = 1
                    ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                    escolhido.DataCadastro,
                    NULL
                FROM UsuariosOrdenados escolhido
                WHERE escolhido.Ordem = 1;

                ;WITH VinculosOrdenados AS
                (
                    SELECT
                        u.Id AS UserId,
                        u.ClinicaId,
                        u.PerfilId,
                        u.Ativo,
                        u.DataCadastro,
                        ug.Id AS UsuarioGlobalId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY ug.Id
                            ORDER BY CASE WHEN u.ClinicaId = 1 THEN 0 ELSE 1 END, u.Id
                        ) AS Ordem
                    FROM Users u
                    INNER JOIN UsuariosGlobais ug
                        ON ug.Email = LOWER(LTRIM(RTRIM(u.Email)))
                )
                INSERT INTO UsuariosClinicas
                    (UsuarioGlobalId, ClinicaId, UserId, PerfilId, Ativo, ClinicaPadrao, DataCadastro, DataAtualizacao)
                SELECT
                    UsuarioGlobalId,
                    ClinicaId,
                    UserId,
                    PerfilId,
                    Ativo,
                    CASE WHEN Ordem = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                    DataCadastro,
                    NULL
                FROM VinculosOrdenados;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_Acao_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "Acao", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_ClinicaId_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "ClinicaId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_DataCadastro",
                table: "AuditoriasPlataforma",
                column: "DataCadastro");

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_UsuarioGlobalId_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "UsuarioGlobalId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_ClinicaId_PerfilId_Ativo",
                table: "UsuariosClinicas",
                columns: new[] { "ClinicaId", "PerfilId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_PerfilId",
                table: "UsuariosClinicas",
                column: "PerfilId");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_UserId",
                table: "UsuariosClinicas",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_UsuarioGlobalId_ClinicaId",
                table: "UsuariosClinicas",
                columns: new[] { "UsuarioGlobalId", "ClinicaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_Ativo",
                table: "UsuariosGlobais",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_Email",
                table: "UsuariosGlobais",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriasPlataforma");

            migrationBuilder.DropTable(
                name: "UsuariosClinicas");

            migrationBuilder.DropTable(
                name: "UsuariosGlobais");
        }
    }
}
