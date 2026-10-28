-- Read-only. Run as the ACTUAL runtime identity in the application database.
-- No principal names, credentials, row data or connection strings are returned.
SET NOCOUNT ON;
IF USER_NAME() IN (N'dbo', N'guest')
    OR ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0)=1
    OR NOT EXISTS (SELECT 1 FROM sys.user_token WHERE name=N'hemodinks_runtime' AND type=N'ROLE')
    OR EXISTS (SELECT 1 FROM sys.user_token WHERE type=N'ROLE' AND name NOT IN (N'public',N'hemodinks_runtime'))
    OR EXISTS (SELECT 1 FROM sys.fn_my_permissions(NULL, N'DATABASE')
        WHERE permission_name NOT IN (N'CONNECT', N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION', N'VIEW ANY COLUMN MASTER KEY DEFINITION'))
    OR EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id=USER_ID())
    OR EXISTS (SELECT 1 FROM sys.objects WHERE principal_id=USER_ID())
    OR EXISTS (SELECT 1 FROM sys.database_principals WHERE owning_principal_id=USER_ID())
    OR EXISTS (SELECT 1 FROM sys.database_permissions p JOIN sys.user_token t ON p.grantee_principal_id=t.principal_id
        WHERE p.state='W' OR (p.class=4 AND p.state='G')
        OR (p.class=1 AND p.major_id=OBJECT_ID(N'dbo.__EFMigrationsHistory') AND p.state='G'
            AND p.permission_name IN (N'INSERT',N'UPDATE',N'DELETE')))
    OR EXISTS (SELECT 1 FROM sys.database_principals WHERE principal_id=USER_ID() AND authentication_type=1)
    OR EXISTS (SELECT 1 FROM sys.schemas s CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name), N'SCHEMA') p
        WHERE s.name NOT IN (N'sys', N'INFORMATION_SCHEMA')
        AND (s.name<>N'dbo' OR p.permission_name NOT IN (N'SELECT',N'INSERT',N'UPDATE',N'DELETE')))
    OR EXISTS (SELECT 1 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
        CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name)+N'.'+QUOTENAME(o.name), N'OBJECT') p
        WHERE o.is_ms_shipped=0 AND (s.name<>N'dbo' OR p.permission_name NOT IN (N'SELECT',N'INSERT',N'UPDATE',N'DELETE')))
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'SELECT'),0)<>1
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'),0)<>1
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'UPDATE'),0)<>1
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'),0)<>1
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'SELECT'),0)<>1
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'INSERT'),1)<>0
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'UPDATE'),1)<>0
    OR ISNULL(HAS_PERMS_BY_NAME(N'dbo.__EFMigrationsHistory', N'OBJECT', N'DELETE'),1)<>0
    THROW 51972, 'Runtime privilege audit failed; review identity, effective permissions, roles and ownership before cutover.', 1;
SELECT CAST(1 AS int) AS RuntimePrivilegeAuditPassed;
