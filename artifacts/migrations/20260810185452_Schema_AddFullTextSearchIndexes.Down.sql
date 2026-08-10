IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'GruposMedicos', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[GruposMedicos]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[GruposMedicos];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'GruposMedicos';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'PacienteProcedimentos', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[PacienteProcedimentos]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[PacienteProcedimentos];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'PacienteProcedimentos';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'OPME', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[OPME]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[OPME];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'OPME';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'Convenios', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[Convenios]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[Convenios];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Convenios';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'Hospitais', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[Hospitais]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[Hospitais];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Hospitais';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'Users', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[Users]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[Users];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Users';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'Pacientes', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[Pacientes]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[Pacientes];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Pacientes';
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.fn_listextendedproperty(
        N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated', N'SCHEMA', N'dbo', N'TABLE', N'CBHPMGeral', NULL, NULL))
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fi.object_id = OBJECT_ID(N'[dbo].[CBHPMGeral]')
            AND fc.[name] = N'HemodinksFullTextCatalog')
        DROP FULLTEXT INDEX ON [dbo].[CBHPMGeral];

    EXEC sys.sp_dropextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'CBHPMGeral';
END;
GO

IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND [name] = N'Hemodinks_Schema_AddFullTextSearchIndexes_CatalogCreated')
    AND EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE [name] = N'HemodinksFullTextCatalog')
    AND NOT EXISTS (
        SELECT 1
        FROM sys.fulltext_indexes AS fi
        INNER JOIN sys.fulltext_catalogs AS fc ON fc.fulltext_catalog_id = fi.fulltext_catalog_id
        WHERE fc.[name] = N'HemodinksFullTextCatalog')
BEGIN
    EXEC sys.sp_dropextendedproperty @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_CatalogCreated';
    DROP FULLTEXT CATALOG [HemodinksFullTextCatalog];
END;
GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20260810185452_Schema_AddFullTextSearchIndexes';
GO

