using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_AddFullTextSearchIndexes : Migration
    {
        private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
        private const string CatalogName = "HemodinksFullTextCatalog";
        private const string CatalogMarker = "Hemodinks_Schema_AddFullTextSearchIndexes_CatalogCreated";
        private const string IndexMarker = "Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != SqlServerProvider)
            {
                return;
            }

            migrationBuilder.Sql($$"""
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') <> 1
                    THROW 51000, 'SQL Server Full-Text Search nao esta instalado nesta instancia.', 1;

                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE [name] = N'{{CatalogName}}')
                BEGIN
                    CREATE FULLTEXT CATALOG [{{CatalogName}}] WITH ACCENT_SENSITIVITY = OFF;

                    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND [name] = N'{{CatalogMarker}}')
                        EXEC sys.sp_addextendedproperty @name = N'{{CatalogMarker}}', @value = 1;
                END;
                """, suppressTransaction: true);

            CreateFullTextIndex(
                migrationBuilder,
                "CBHPMGeral",
                "PK_CBHPMGeral",
                "Id",
                ["Procedimento", "Grupo"]);
            CreateFullTextIndex(
                migrationBuilder,
                "Pacientes",
                "PK_Pacientes",
                "Id",
                [
                    "NomePaciente", "Diagnostico", "Hospital", "Medico", "MedicoAuxiliar1",
                    "MedicoAuxiliar2", "Convenio", "OpmeFornecedor", "Procedimento"
                ]);
            CreateFullTextIndex(migrationBuilder, "Users", "PK_Users", "Id", ["Nome"]);
            CreateFullTextIndex(migrationBuilder, "Hospitais", "PK_Hospitais", "Id", ["Nome"]);
            CreateFullTextIndex(
                migrationBuilder,
                "Convenios",
                "PK_Convenios",
                "IdConvenio",
                ["DescricaoConvenio"]);
            CreateFullTextIndex(migrationBuilder, "OPME", "PK_OPME", "IdFornecedor", ["Fornecedor"]);
            CreateFullTextIndex(
                migrationBuilder,
                "PacienteProcedimentos",
                "PK_PacienteProcedimentos",
                "Id",
                ["Procedimento"]);
            CreateFullTextIndex(migrationBuilder, "GruposMedicos", "PK_GruposMedicos", "Id", ["Nome"]);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != SqlServerProvider)
            {
                return;
            }

            DropFullTextIndex(migrationBuilder, "GruposMedicos");
            DropFullTextIndex(migrationBuilder, "PacienteProcedimentos");
            DropFullTextIndex(migrationBuilder, "OPME");
            DropFullTextIndex(migrationBuilder, "Convenios");
            DropFullTextIndex(migrationBuilder, "Hospitais");
            DropFullTextIndex(migrationBuilder, "Users");
            DropFullTextIndex(migrationBuilder, "Pacientes");
            DropFullTextIndex(migrationBuilder, "CBHPMGeral");

            migrationBuilder.Sql($$"""
                IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND [name] = N'{{CatalogMarker}}')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE [name] = N'{{CatalogName}}')
                        EXEC sys.sp_dropextendedproperty @name = N'{{CatalogMarker}}';
                    ELSE IF NOT EXISTS (
                        SELECT 1
                        FROM sys.fulltext_indexes AS fi
                        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
                        WHERE fc.[name] = N'{{CatalogName}}')
                    BEGIN
                        EXEC sys.sp_dropextendedproperty @name = N'{{CatalogMarker}}';
                        DROP FULLTEXT CATALOG [{{CatalogName}}];
                    END;
                END;
                """, suppressTransaction: true);

        }

        private static void CreateFullTextIndex(
            MigrationBuilder migrationBuilder,
            string tableName,
            string keyIndexName,
            string keyColumnName,
            IReadOnlyCollection<string> columns)
        {
            var fullTextColumns = string.Join(
                ", ",
                columns.Select(column => $"[{column}] LANGUAGE 1046"));

            migrationBuilder.Sql($$"""
                IF OBJECT_ID(N'[dbo].[{{tableName}}]', N'U') IS NULL
                    THROW 51001, 'Tabela dbo.{{tableName}} nao encontrada para criacao do indice Full-Text.', 1;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes AS i
                    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
                    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE s.[name] = N'dbo'
                        AND t.[name] = N'{{tableName}}'
                        AND i.[name] = N'{{keyIndexName}}'
                        AND i.is_unique = 1
                        AND i.is_disabled = 0
                        AND i.has_filter = 0
                        AND c.[name] = N'{{keyColumnName}}'
                        AND c.is_nullable = 0
                    GROUP BY i.object_id, i.index_id
                    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
                    THROW 51002, 'KEY INDEX {{keyIndexName}} deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[{{tableName}}]'))
                BEGIN
                    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[{{tableName}}] ({{fullTextColumns}}) KEY INDEX [{{keyIndexName}}] ON [{{CatalogName}}] WITH CHANGE_TRACKING AUTO;');
                    IF NOT EXISTS (
                        SELECT 1
                        FROM sys.fn_listextendedproperty(
                            N'{{IndexMarker}}', N'SCHEMA', N'dbo', N'TABLE', N'{{tableName}}', NULL, NULL))
                        EXEC sys.sp_addextendedproperty
                            @name = N'{{IndexMarker}}',
                            @value = 1,
                            @level0type = N'SCHEMA', @level0name = N'dbo',
                            @level1type = N'TABLE', @level1name = N'{{tableName}}';
                END;
                """, suppressTransaction: true);
        }

        private static void DropFullTextIndex(MigrationBuilder migrationBuilder, string tableName)
        {
            migrationBuilder.Sql($$"""
                IF EXISTS (
                    SELECT 1
                    FROM sys.fn_listextendedproperty(
                        N'{{IndexMarker}}', N'SCHEMA', N'dbo', N'TABLE', N'{{tableName}}', NULL, NULL))
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM sys.fulltext_indexes AS fi
                        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
                        WHERE fi.object_id = OBJECT_ID(N'[dbo].[{{tableName}}]')
                            AND fc.[name] = N'{{CatalogName}}')
                        DROP FULLTEXT INDEX ON [dbo].[{{tableName}}];

                    EXEC sys.sp_dropextendedproperty
                        @name = N'{{IndexMarker}}',
                        @level0type = N'SCHEMA', @level0name = N'dbo',
                        @level1type = N'TABLE', @level1name = N'{{tableName}}';
                END;
                """, suppressTransaction: true);
        }
    }
}
