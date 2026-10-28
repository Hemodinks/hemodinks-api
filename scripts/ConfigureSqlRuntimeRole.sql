-- Administrator-only, reviewed operational step; never run at API startup.
-- Create a dedicated contained user separately (Entra preferred); no password here.
-- Set Runtime197ExpectedDatabase and Runtime197User in this same session first.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @expected nvarchar(128) = CONVERT(nvarchar(128), SESSION_CONTEXT(N'Runtime197ExpectedDatabase'));
DECLARE @user nvarchar(128) = CONVERT(nvarchar(128), SESSION_CONTEXT(N'Runtime197User'));
IF @expected IS NULL OR @expected COLLATE Latin1_General_100_BIN2 <> DB_NAME() COLLATE Latin1_General_100_BIN2
    OR DATALENGTH(@expected) <> DATALENGTH(DB_NAME()) OR DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51970, 'Runtime role blocked: confirm the exact application database in this session.', 1;
DECLARE @id int = DATABASE_PRINCIPAL_ID(@user);
IF @id IS NULL OR @id <= 4 OR NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE principal_id=@id AND type IN ('S','E','X') AND authentication_type<>1)
    OR EXISTS (SELECT 1 FROM sys.database_role_members WHERE member_principal_id=@id AND role_principal_id<>ISNULL(DATABASE_PRINCIPAL_ID(N'hemodinks_runtime'), -1))
    OR EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id=@id)
    OR EXISTS (SELECT 1 FROM sys.objects WHERE principal_id=@id)
    OR EXISTS (SELECT 1 FROM sys.database_principals WHERE owning_principal_id=@id)
    OR EXISTS (SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id=@id AND NOT (class=0 AND permission_name=N'CONNECT' AND state='G'))
    THROW 51971, 'Runtime role blocked: use a dedicated unprivileged user without ownership or other grants.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51971, 'Runtime role blocked: apply reviewed migrations with the migration identity first.', 1;
DECLARE @role int=DATABASE_PRINCIPAL_ID(N'hemodinks_runtime');
IF @role IS NOT NULL AND (
    NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE principal_id=@role AND type='R' AND owning_principal_id=DATABASE_PRINCIPAL_ID(N'dbo'))
    OR EXISTS (SELECT 1 FROM sys.database_role_members WHERE member_principal_id=@role)
    OR EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id=@role)
    OR EXISTS (SELECT 1 FROM sys.objects WHERE principal_id=@role)
    OR EXISTS (SELECT 1 FROM sys.database_principals WHERE owning_principal_id=@role)
    OR EXISTS (SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id=@role AND NOT (
        (class=3 AND major_id=SCHEMA_ID(N'dbo') AND state='G' AND permission_name IN (N'SELECT',N'INSERT',N'UPDATE',N'DELETE'))
        OR (class=1 AND major_id=OBJECT_ID(N'dbo.__EFMigrationsHistory') AND minor_id=0 AND state='D' AND permission_name IN (N'INSERT',N'UPDATE',N'DELETE')))))
    THROW 51971, 'Runtime role blocked: existing role has unexpected permissions, membership or ownership.', 1;

BEGIN TRANSACTION;
IF DATABASE_PRINCIPAL_ID(N'hemodinks_runtime') IS NULL
    EXEC(N'CREATE ROLE hemodinks_runtime AUTHORIZATION dbo');
-- Schema scope covers future application tables, while the history stays read-only.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO hemodinks_runtime;
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.__EFMigrationsHistory TO hemodinks_runtime;
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members WHERE member_principal_id=@id AND role_principal_id=DATABASE_PRINCIPAL_ID(N'hemodinks_runtime'))
BEGIN
    DECLARE @sql nvarchar(max)=N'ALTER ROLE hemodinks_runtime ADD MEMBER '+QUOTENAME(@user)+N';';
    EXEC sys.sp_executesql @sql;
END;
COMMIT;
PRINT 'Runtime role configured; validate effective privileges with the runtime connection before cutover.';
