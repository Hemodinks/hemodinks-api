using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Estado que a cadeia antiga deixava no banco e o modelo do EF não descreve. Sem isto, um banco novo
    /// ficaria diferente dos existentes (comparação em docs/migrations-baseline.md). As tabelas são
    /// removidas no Down da baseline, então não há Down próprio.
    /// </summary>
    public partial class Baseline
    {
        private static void AddLegacyState(MigrationBuilder migrationBuilder)
        {
            // Defaults criados por AddColumn(defaultValue) para preencher linhas existentes.
            migrationBuilder.Sql("""
                ALTER TABLE [AuthenticationSessions] ADD DEFAULT '00000000-0000-0000-0000-000000000000' FOR [SecurityVersion];
                ALTER TABLE [EquipeLoginDesafios] ADD DEFAULT '00000000-0000-0000-0000-000000000000' FOR [SecurityVersion];
                ALTER TABLE [UsuariosGlobais] ADD DEFAULT '00000000-0000-0000-0000-000000000000' FOR [SecurityVersion];
                ALTER TABLE [UsuariosGlobais] ADD DEFAULT CAST(0 AS bit) FOR [TemporaryPasswordRecovery];
                """);

            // Criado em 20260713160359_AddFaturamentoMedicoCompetencia e nunca removido do banco.
            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_CompetenciaInicio_CompetenciaFinal",
                table: "FaturamentosMedicos",
                columns: new[] { "CompetenciaInicio", "CompetenciaFinal" });

            // De 20260720175703_HardenMultiClinicTenancyAndPlatformAdmin.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE [Clinicas] SET [TrialAte] = DATEADD(day, 14, SYSUTCDATETIME()) WHERE [TrialAte] IS NULL;');");

            // De 20260721145022_MoveClinicBrandingToClinicCrud.
            migrationBuilder.Sql("EXEC(N'" +
                """
                UPDATE clinica
                SET
                    clinica.Nome = CASE
                        WHEN configuracao.NomeEmpresa IS NOT NULL
                          AND LTRIM(RTRIM(configuracao.NomeEmpresa)) <> ''
                        THEN LTRIM(RTRIM(configuracao.NomeEmpresa))
                        ELSE clinica.Nome
                    END,
                    clinica.FotoClinica = CASE
                        WHEN configuracao.FotoEmpresa IS NOT NULL
                          AND LTRIM(RTRIM(configuracao.FotoEmpresa)) <> ''
                        THEN configuracao.FotoEmpresa
                        ELSE clinica.FotoClinica
                    END
                FROM Clinicas clinica
                INNER JOIN ConfiguracoesSistema configuracao
                    ON configuracao.ClinicaId = clinica.Id;
                """.Replace("'", "''") + "');");
        }
    }
}
