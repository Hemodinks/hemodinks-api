using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AddSurgeryType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TipoCirurgia",
                table: "Pacientes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Eletiva");

            migrationBuilder.AddColumn<string>(
                name: "TipoCirurgia",
                table: "AtendimentosCirurgicos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Eletiva");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos",
                sql: "[TipoCirurgia] IN (N'Eletiva', N'Urgencia')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Application rollback retains the additive schema. A schema rollback must
            // never erase urgency classifications. Locks protect the preflight until
            // the EF migration transaction finishes, including against old writers.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Pacientes] WITH (TABLOCKX, HOLDLOCK)
                    WHERE [TipoCirurgia] <> N'Eletiva')
                    THROW 50000, 'Rollback bloqueado: pacientes possuem tipos de cirurgia que precisam ser preservados. Mantenha o schema expandido.', 1;

                IF EXISTS (SELECT 1 FROM [AtendimentosCirurgicos] WITH (TABLOCKX, HOLDLOCK)
                    WHERE [TipoCirurgia] <> N'Eletiva')
                    THROW 50000, 'Rollback bloqueado: atendimentos possuem tipos de cirurgia que precisam ser preservados. Mantenha o schema expandido.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pacientes_TipoCirurgia",
                table: "Pacientes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_TipoCirurgia",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropColumn(
                name: "TipoCirurgia",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "TipoCirurgia",
                table: "AtendimentosCirurgicos");
        }
    }
}
