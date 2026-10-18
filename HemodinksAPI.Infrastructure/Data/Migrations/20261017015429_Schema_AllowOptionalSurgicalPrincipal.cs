using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AllowOptionalSurgicalPrincipal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_MedicosDistintos",
                table: "AtendimentosCirurgicos");

            migrationBuilder.AlterColumn<int>(
                name: "MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_AuxiliaresDistintos",
                table: "AtendimentosCirurgicos",
                sql: "[MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar2Id] IS NULL OR [MedicoAuxiliar1Id] <> [MedicoAuxiliar2Id]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_EquipeObrigatoria",
                table: "AtendimentosCirurgicos",
                sql: "[MedicoResponsavelId] IS NOT NULL OR [MedicoAuxiliar1Id] IS NOT NULL OR [MedicoAuxiliar2Id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never invent a principal or discard a doctor's role to fit the old schema.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AtendimentosCirurgicos]
                    WHERE [MedicoResponsavelId] IS NULL
                       OR [MedicoResponsavelId] = [MedicoAuxiliar1Id]
                       OR [MedicoResponsavelId] = [MedicoAuxiliar2Id])
                    THROW 50000, 'Rollback bloqueado: existem equipes cirurgicas incompativeis com o schema anterior.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_AuxiliaresDistintos",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AtendimentosCirurgicos_EquipeObrigatoria",
                table: "AtendimentosCirurgicos");

            migrationBuilder.AlterColumn<int>(
                name: "MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AtendimentosCirurgicos_MedicosDistintos",
                table: "AtendimentosCirurgicos",
                sql: "([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar1Id] <> [MedicoResponsavelId]) AND ([MedicoAuxiliar2Id] IS NULL OR ([MedicoAuxiliar2Id] <> [MedicoResponsavelId] AND ([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar2Id] <> [MedicoAuxiliar1Id])))");
        }
    }
}
