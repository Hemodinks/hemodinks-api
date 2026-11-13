-- Somente leitura. Lista schema e dados de um banco para comparar com outro via diff (#231).
-- Ignora ordem fisica de colunas, nomes automaticos de DF__ e o valor de Clinicas.TrialAte (depende da hora).
-- Uso: sqlcmd -I -y 0 -d <Banco> -i scripts/CompareDatabaseSchema.sql > banco.txt  (repita no outro banco e compare).
SET NOCOUNT ON;
DECLARE @out TABLE (line nvarchar(max));

INSERT @out SELECT CONCAT('column|', OBJECT_NAME(c.object_id), '|', c.name, '|', TYPE_NAME(c.user_type_id), '|',
    c.max_length, '|', c.precision, '|', c.scale, '|', c.is_nullable, '|', c.is_identity, '|', c.collation_name, '|',
    d.definition, '|', cc.definition)
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
WHERE t.is_ms_shipped = 0;


INSERT @out SELECT CONCAT('index|', OBJECT_NAME(i.object_id), '|', i.name, '|', i.is_unique, '|', i.type, '|', i.is_primary_key, '|',
    i.is_unique_constraint, '|', i.filter_definition, '|', COL_NAME(ic.object_id, ic.column_id), '|', ic.key_ordinal, '|',
    ic.is_descending_key, '|', ic.is_included_column)
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.tables t ON t.object_id = i.object_id WHERE t.is_ms_shipped = 0;

INSERT @out SELECT CONCAT('fk|', fk.name, '|', OBJECT_NAME(fk.parent_object_id), '|', COL_NAME(fc.parent_object_id, fc.parent_column_id),
    '|', OBJECT_NAME(fk.referenced_object_id), '|', COL_NAME(fc.referenced_object_id, fc.referenced_column_id), '|',
    fk.delete_referential_action, '|', fk.update_referential_action, '|', fk.is_disabled, '|', fk.is_not_trusted, '|', fc.constraint_column_id)
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = fk.object_id;

INSERT @out SELECT CONCAT('check|', OBJECT_NAME(parent_object_id), '|', name, '|', definition, '|', is_disabled, '|', is_not_trusted)
FROM sys.check_constraints;

INSERT @out SELECT CONCAT('module|', o.type_desc, '|', o.name, '|', m.definition)
FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id WHERE o.is_ms_shipped = 0;

INSERT @out SELECT CONCAT('trigger|', name, '|', OBJECT_NAME(parent_id), '|', is_disabled) FROM sys.triggers;
INSERT @out SELECT CONCAT('sequence|', name, '|', CONVERT(nvarchar(100), start_value)) FROM sys.sequences;
INSERT @out SELECT CONCAT('schema|', name) FROM sys.schemas WHERE schema_id BETWEEN 5 AND 16383;
INSERT @out SELECT CONCAT('table|', name) FROM sys.tables WHERE is_ms_shipped = 0;

INSERT @out SELECT CONCAT('fts-catalog|', name, '|', is_accent_sensitivity_on, '|', is_default) FROM sys.fulltext_catalogs;
INSERT @out SELECT CONCAT('fts|', OBJECT_NAME(f.object_id), '|', c.name, '|', f.change_tracking_state, '|',
    COALESCE(CONVERT(varchar, f.stoplist_id), 'NULL'), '|', i.name, '|', COL_NAME(fc.object_id, fc.column_id), '|', fc.language_id)
FROM sys.fulltext_indexes f JOIN sys.fulltext_catalogs c ON c.fulltext_catalog_id = f.fulltext_catalog_id
JOIN sys.indexes i ON i.object_id = f.object_id AND i.index_id = f.unique_index_id
JOIN sys.fulltext_index_columns fc ON fc.object_id = f.object_id;

INSERT @out SELECT CONCAT('extprop|', class_desc, '|', COALESCE(OBJECT_NAME(major_id), ''), '|', name, '|', CONVERT(nvarchar(4000), value))
FROM sys.extended_properties;

DECLARE @t sysname, @sql nvarchar(max), @json nvarchar(max), @count int;
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.tables WHERE is_ms_shipped = 0 AND name <> '__EFMigrationsHistory' ORDER BY name;
OPEN tables;
FETCH NEXT FROM tables INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @cols nvarchar(max) = (SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(name)), ',') WITHIN GROUP (ORDER BY name)
        FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND NOT (@t = 'Clinicas' AND name = 'TrialAte'));
    DECLARE @key nvarchar(300) = (SELECT QUOTENAME(COL_NAME(ic.object_id, ic.column_id)) FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
        WHERE i.object_id = OBJECT_ID(@t) AND i.is_primary_key = 1);
    SET @sql = N'SELECT @c = COUNT(*), @j = (SELECT ' + @cols + N' FROM ' + QUOTENAME(@t) + N' ORDER BY ' + COALESCE(@key, N'1') + N' FOR JSON PATH, INCLUDE_NULL_VALUES) FROM ' + QUOTENAME(@t);
    EXEC sp_executesql @sql, N'@c int OUTPUT, @j nvarchar(max) OUTPUT', @c = @count OUTPUT, @j = @json OUTPUT;
    INSERT @out VALUES (CONCAT('rows|', @t, '|', @count, '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', COALESCE(@json, N'')), 2)));
    FETCH NEXT FROM tables INTO @t;
END;
CLOSE tables;

INSERT @out SELECT CONCAT('trial|', COUNT(*)) FROM Clinicas
    WHERE TrialAte BETWEEN DATEADD(day, 13, SYSUTCDATETIME()) AND DATEADD(day, 15, SYSUTCDATETIME());
SELECT line FROM @out ORDER BY line;
