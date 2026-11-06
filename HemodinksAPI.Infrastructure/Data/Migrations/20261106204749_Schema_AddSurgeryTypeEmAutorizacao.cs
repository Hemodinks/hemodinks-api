using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AddSurgeryTypeEmAutorizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia', N'EmAutorizacao')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia', N'EmAutorizacao')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A schema rollback must never erase or reclassify these records. Locks protect the
            // preflight until the EF migration transaction finishes, including against old writers.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Pacientes] WITH (TABLOCKX, HOLDLOCK)
                    WHERE [TipoCirurgia] = N'EmAutorizacao')
                    THROW 50000, 'Rollback bloqueado: pacientes possuem cirurgias em processo de autorizacao que precisam ser preservadas. Mantenha o schema expandido.', 1;

                IF EXISTS (SELECT 1 FROM [AtendimentosCirurgicos] WITH (TABLOCKX, HOLDLOCK)
                    WHERE [TipoCirurgia] = N'EmAutorizacao')
                    THROW 50000, 'Rollback bloqueado: atendimentos possuem cirurgias em processo de autorizacao que precisam ser preservadas. Mantenha o schema expandido.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia')");
        }
    }
}
