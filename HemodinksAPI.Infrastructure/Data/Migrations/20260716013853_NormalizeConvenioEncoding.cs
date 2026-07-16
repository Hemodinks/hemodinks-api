using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeConvenioEncoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Convenios]
                SET [DescricaoConvenio] = N'Bradesco Saúde'
                WHERE [IdConvenio] = 2
                    AND [DescricaoConvenio] <> N'Bradesco Saúde';

                UPDATE [Convenios]
                SET [DescricaoConvenio] = N'Cemig Saúde'
                WHERE [IdConvenio] = 3
                    AND [DescricaoConvenio] <> N'Cemig Saúde';

                UPDATE [Convenios]
                SET [DescricaoConvenio] = N'Sul América'
                WHERE [IdConvenio] = 8
                    AND [DescricaoConvenio] <> N'Sul América';

                UPDATE [Convenios]
                SET [DescricaoConvenio] = N'Unimed Uberlândia - Plano  Unimed Intercâmbio'
                WHERE [IdConvenio] = 9
                    AND [DescricaoConvenio] <> N'Unimed Uberlândia - Plano  Unimed Intercâmbio';

                DECLARE @Correcoes TABLE
                (
                    [ValorCorrompido] nvarchar(255) NOT NULL,
                    [ValorCorreto] nvarchar(255) NOT NULL
                );

                INSERT INTO @Correcoes ([ValorCorrompido], [ValorCorreto])
                VALUES
                    (N'Bradesco Sa' + NCHAR(195) + NCHAR(186) + N'de', N'Bradesco Saúde'),
                    (N'Bradesco Sa' + NCHAR(65533) + N'de', N'Bradesco Saúde'),
                    (N'Cemig Sa' + NCHAR(195) + NCHAR(186) + N'de', N'Cemig Saúde'),
                    (N'Cemig Sa' + NCHAR(65533) + N'de', N'Cemig Saúde'),
                    (N'Sul Am' + NCHAR(195) + NCHAR(169) + N'rica', N'Sul América'),
                    (N'Sul Am' + NCHAR(65533) + N'rica', N'Sul América'),
                    (
                        N'Unimed Uberl' + NCHAR(195) + NCHAR(162) + N'ndia - Plano  Unimed Interc' + NCHAR(195) + NCHAR(162) + N'mbio',
                        N'Unimed Uberlândia - Plano  Unimed Intercâmbio'
                    ),
                    (
                        N'Unimed Uberl' + NCHAR(65533) + N'ndia - Plano  Unimed Interc' + NCHAR(65533) + N'mbio',
                        N'Unimed Uberlândia - Plano  Unimed Intercâmbio'
                    );

                UPDATE pacientes
                SET [Convenio] = correcoes.[ValorCorreto]
                FROM [Pacientes] pacientes
                INNER JOIN @Correcoes correcoes
                    ON pacientes.[Convenio] = correcoes.[ValorCorrompido];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data cleanup is intentionally not reverted.
        }
    }
}
