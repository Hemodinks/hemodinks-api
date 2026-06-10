using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260610234500_EnsureEventReminderColumns")]
    public partial class EnsureEventReminderColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[Events]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Events] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] int NOT NULL,
                        [MedicalUserId] int NULL,
                        [Title] nvarchar(255) NOT NULL,
                        [Description] nvarchar(2000) NULL,
                        [Start] datetime2 NOT NULL,
                        [End] datetime2 NOT NULL,
                        [NotifyMedicalProfile] bit NOT NULL CONSTRAINT [DF_Events_NotifyMedicalProfile] DEFAULT CAST(0 AS bit),
                        [NotifyUser] bit NOT NULL CONSTRAINT [DF_Events_NotifyUser] DEFAULT CAST(0 AS bit),
                        [ReminderPeriodMinutes] int NULL,
                        [LastReminderSentAt] datetime2 NULL,
                        [NextReminderAt] datetime2 NULL,
                        [IsCompleted] bit NOT NULL CONSTRAINT [DF_Events_IsCompleted] DEFAULT CAST(0 AS bit),
                        [CompletedAt] datetime2 NULL,
                        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Events_CreatedAt] DEFAULT GETUTCDATE(),
                        [UpdatedAt] datetime2 NULL,
                        CONSTRAINT [PK_Events] PRIMARY KEY ([Id])
                    );
                END;

                IF COL_LENGTH(N'[dbo].[Events]', N'MedicalUserId') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [MedicalUserId] int NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'NotifyMedicalProfile') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [NotifyMedicalProfile] bit NOT NULL CONSTRAINT [DF_Events_NotifyMedicalProfile] DEFAULT CAST(0 AS bit);

                IF COL_LENGTH(N'[dbo].[Events]', N'NotifyUser') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [NotifyUser] bit NOT NULL CONSTRAINT [DF_Events_NotifyUser] DEFAULT CAST(0 AS bit);

                IF COL_LENGTH(N'[dbo].[Events]', N'ReminderPeriodMinutes') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [ReminderPeriodMinutes] int NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'LastReminderSentAt') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [LastReminderSentAt] datetime2 NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'NextReminderAt') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [NextReminderAt] datetime2 NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'IsCompleted') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [IsCompleted] bit NOT NULL CONSTRAINT [DF_Events_IsCompleted] DEFAULT CAST(0 AS bit);

                IF COL_LENGTH(N'[dbo].[Events]', N'CompletedAt') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [CompletedAt] datetime2 NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'CreatedAt') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Events_CreatedAt] DEFAULT GETUTCDATE();

                IF COL_LENGTH(N'[dbo].[Events]', N'UpdatedAt') IS NULL
                    ALTER TABLE [dbo].[Events] ADD [UpdatedAt] datetime2 NULL;

                IF COL_LENGTH(N'[dbo].[Events]', N'MedicalUserId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.foreign_keys
                        WHERE [name] = N'FK_Events_Users_MedicalUserId'
                            AND [parent_object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                BEGIN
                    ALTER TABLE [dbo].[Events] WITH CHECK ADD CONSTRAINT [FK_Events_Users_MedicalUserId]
                        FOREIGN KEY ([MedicalUserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION;
                END;

                IF COL_LENGTH(N'[dbo].[Events]', N'UserId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.foreign_keys
                        WHERE [name] = N'FK_Events_Users_UserId'
                            AND [parent_object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                BEGIN
                    ALTER TABLE [dbo].[Events] WITH CHECK ADD CONSTRAINT [FK_Events_Users_UserId]
                        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE;
                END;

                IF COL_LENGTH(N'[dbo].[Events]', N'MedicalUserId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [name] = N'IX_Events_MedicalUserId'
                            AND [object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                    CREATE INDEX [IX_Events_MedicalUserId] ON [dbo].[Events] ([MedicalUserId]);

                IF COL_LENGTH(N'[dbo].[Events]', N'NextReminderAt') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[Events]', N'IsCompleted') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [name] = N'IX_Events_NextReminderAt_IsCompleted'
                            AND [object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                    CREATE INDEX [IX_Events_NextReminderAt_IsCompleted] ON [dbo].[Events] ([NextReminderAt], [IsCompleted]);

                IF COL_LENGTH(N'[dbo].[Events]', N'Start') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[Events]', N'End') IS NOT NULL
                    AND COL_LENGTH(N'[dbo].[Events]', N'IsCompleted') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [name] = N'IX_Events_Start_End_IsCompleted'
                            AND [object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                    CREATE INDEX [IX_Events_Start_End_IsCompleted] ON [dbo].[Events] ([Start], [End], [IsCompleted]);

                IF COL_LENGTH(N'[dbo].[Events]', N'UserId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [name] = N'IX_Events_UserId'
                            AND [object_id] = OBJECT_ID(N'[dbo].[Events]')
                    )
                    CREATE INDEX [IX_Events_UserId] ON [dbo].[Events] ([UserId]);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: this repair migration must not remove production agenda data.
        }
    }
}
