IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') <> 1
    THROW 51000, 'SQL Server Full-Text Search nao esta instalado nesta instancia.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE [name] = N'HemodinksFullTextCatalog')
BEGIN
    CREATE FULLTEXT CATALOG [HemodinksFullTextCatalog] WITH ACCENT_SENSITIVITY = OFF;

    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND [name] = N'Hemodinks_Schema_AddFullTextSearchIndexes_CatalogCreated')
        EXEC sys.sp_addextendedproperty @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_CatalogCreated', @value = 1;
END;
GO

IF OBJECT_ID(N'[dbo].[CBHPMGeral]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.CBHPMGeral nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'CBHPMGeral'
        AND i.[name] = N'PK_CBHPMGeral'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_CBHPMGeral deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[CBHPMGeral]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[CBHPMGeral] ([Procedimento] LANGUAGE 1046, [Grupo] LANGUAGE 1046) KEY INDEX [PK_CBHPMGeral] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'CBHPMGeral';
END;
GO

IF OBJECT_ID(N'[dbo].[Pacientes]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.Pacientes nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'Pacientes'
        AND i.[name] = N'PK_Pacientes'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_Pacientes deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[Pacientes]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Pacientes] ([NomePaciente] LANGUAGE 1046, [Diagnostico] LANGUAGE 1046, [Hospital] LANGUAGE 1046, [Medico] LANGUAGE 1046, [MedicoAuxiliar1] LANGUAGE 1046, [MedicoAuxiliar2] LANGUAGE 1046, [Convenio] LANGUAGE 1046, [OpmeFornecedor] LANGUAGE 1046, [Procedimento] LANGUAGE 1046) KEY INDEX [PK_Pacientes] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Pacientes';
END;
GO

IF OBJECT_ID(N'[dbo].[Users]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.Users nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'Users'
        AND i.[name] = N'PK_Users'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_Users deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[Users]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Users] ([Nome] LANGUAGE 1046) KEY INDEX [PK_Users] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Users';
END;
GO

IF OBJECT_ID(N'[dbo].[Hospitais]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.Hospitais nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'Hospitais'
        AND i.[name] = N'PK_Hospitais'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_Hospitais deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[Hospitais]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Hospitais] ([Nome] LANGUAGE 1046) KEY INDEX [PK_Hospitais] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Hospitais';
END;
GO

IF OBJECT_ID(N'[dbo].[Convenios]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.Convenios nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'Convenios'
        AND i.[name] = N'PK_Convenios'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'IdConvenio'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_Convenios deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[Convenios]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Convenios] ([DescricaoConvenio] LANGUAGE 1046) KEY INDEX [PK_Convenios] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'Convenios';
END;
GO

IF OBJECT_ID(N'[dbo].[OPME]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.OPME nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'OPME'
        AND i.[name] = N'PK_OPME'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'IdFornecedor'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_OPME deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[OPME]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[OPME] ([Fornecedor] LANGUAGE 1046) KEY INDEX [PK_OPME] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'OPME';
END;
GO

IF OBJECT_ID(N'[dbo].[PacienteProcedimentos]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.PacienteProcedimentos nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'PacienteProcedimentos'
        AND i.[name] = N'PK_PacienteProcedimentos'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_PacienteProcedimentos deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[PacienteProcedimentos]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[PacienteProcedimentos] ([Procedimento] LANGUAGE 1046) KEY INDEX [PK_PacienteProcedimentos] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'PacienteProcedimentos';
END;
GO

IF OBJECT_ID(N'[dbo].[GruposMedicos]', N'U') IS NULL
    THROW 51001, 'Tabela dbo.GruposMedicos nao encontrada para criacao do indice Full-Text.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t ON t.object_id = i.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE s.[name] = N'dbo'
        AND t.[name] = N'GruposMedicos'
        AND i.[name] = N'PK_GruposMedicos'
        AND i.is_unique = 1
        AND i.is_disabled = 0
        AND i.has_filter = 0
        AND c.[name] = N'Id'
        AND c.is_nullable = 0
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1 AND MAX(ic.key_ordinal) = 1)
    THROW 51002, 'KEY INDEX PK_GruposMedicos deve ser unico, ativo, sem filtro, de coluna unica e nao nula.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'[dbo].[GruposMedicos]'))
BEGIN
    EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[GruposMedicos] ([Nome] LANGUAGE 1046) KEY INDEX [PK_GruposMedicos] ON [HemodinksFullTextCatalog] WITH CHANGE_TRACKING AUTO;');
    EXEC sys.sp_addextendedproperty
        @name = N'Hemodinks_Schema_AddFullTextSearchIndexes_IndexCreated',
        @value = 1,
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'GruposMedicos';
END;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260810185452_Schema_AddFullTextSearchIndexes', N'10.0.0');
GO

