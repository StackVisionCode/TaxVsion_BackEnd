using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Reminder.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Esquema del <c>AdoJobStore</c> de Quartz.NET 3.x para SQL Server (prefijo <c>QRTZ_</c>).
    ///
    /// <para>
    /// Estas tablas son de Quartz, no del modelo de EF: no aparecen en el snapshot ni en
    /// <c>ReminderDbContext</c>. Van igualmente como migración porque el despliegue del servicio ya
    /// corre <c>dotnet ef database update</c>; dejar el script fuera del pipeline significaría que
    /// una base nueva arranca y el scheduler revienta en el primer <c>ScheduleJob</c>.
    /// </para>
    ///
    /// <para>
    /// El DDL es el <c>tables_sqlserver.sql</c> oficial de Quartz.NET 3.x. <b>No editarlo:</b>
    /// nombres, longitudes y claves los asume el proveedor <c>SqlServerDelegate</c> por posición y
    /// por nombre. Si se sube de major de Quartz, hay que comparar contra el script de esa versión y
    /// agregar una migración nueva, nunca modificar ésta.
    /// </para>
    /// </summary>
    public partial class QuartzAdoJobStoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_CALENDARS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [CALENDAR_NAME] nvarchar(200) NOT NULL,
                    [CALENDAR] varbinary(max) NOT NULL,
                    CONSTRAINT [PK_QRTZ_CALENDARS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [CALENDAR_NAME])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_JOB_DETAILS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [JOB_NAME] nvarchar(150) NOT NULL,
                    [JOB_GROUP] nvarchar(150) NOT NULL,
                    [DESCRIPTION] nvarchar(250) NULL,
                    [JOB_CLASS_NAME] nvarchar(250) NOT NULL,
                    [IS_DURABLE] bit NOT NULL,
                    [IS_NONCONCURRENT] bit NOT NULL,
                    [IS_UPDATE_DATA] bit NOT NULL,
                    [REQUESTS_RECOVERY] bit NOT NULL,
                    [JOB_DATA] varbinary(max) NULL,
                    CONSTRAINT [PK_QRTZ_JOB_DETAILS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [JOB_NAME], [JOB_GROUP])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [JOB_NAME] nvarchar(150) NOT NULL,
                    [JOB_GROUP] nvarchar(150) NOT NULL,
                    [DESCRIPTION] nvarchar(250) NULL,
                    [NEXT_FIRE_TIME] bigint NULL,
                    [PREV_FIRE_TIME] bigint NULL,
                    [PRIORITY] int NULL,
                    [TRIGGER_STATE] nvarchar(16) NOT NULL,
                    [TRIGGER_TYPE] nvarchar(8) NOT NULL,
                    [START_TIME] bigint NOT NULL,
                    [END_TIME] bigint NULL,
                    [CALENDAR_NAME] nvarchar(200) NULL,
                    [MISFIRE_INSTR] int NULL,
                    [JOB_DATA] varbinary(max) NULL,
                    CONSTRAINT [PK_QRTZ_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]),
                    CONSTRAINT [FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS] FOREIGN KEY ([SCHED_NAME], [JOB_NAME], [JOB_GROUP])
                        REFERENCES [dbo].[QRTZ_JOB_DETAILS] ([SCHED_NAME], [JOB_NAME], [JOB_GROUP])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_SIMPLE_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [REPEAT_COUNT] int NOT NULL,
                    [REPEAT_INTERVAL] bigint NOT NULL,
                    [TIMES_TRIGGERED] int NOT NULL,
                    CONSTRAINT [PK_QRTZ_SIMPLE_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]),
                    CONSTRAINT [FK_QRTZ_SIMPLE_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP])
                        REFERENCES [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]) ON DELETE CASCADE
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_CRON_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [CRON_EXPRESSION] nvarchar(120) NOT NULL,
                    [TIME_ZONE_ID] nvarchar(80) NULL,
                    CONSTRAINT [PK_QRTZ_CRON_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]),
                    CONSTRAINT [FK_QRTZ_CRON_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP])
                        REFERENCES [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]) ON DELETE CASCADE
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_SIMPROP_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [STR_PROP_1] nvarchar(512) NULL,
                    [STR_PROP_2] nvarchar(512) NULL,
                    [STR_PROP_3] nvarchar(512) NULL,
                    [INT_PROP_1] int NULL,
                    [INT_PROP_2] int NULL,
                    [LONG_PROP_1] bigint NULL,
                    [LONG_PROP_2] bigint NULL,
                    [DEC_PROP_1] numeric(13,4) NULL,
                    [DEC_PROP_2] numeric(13,4) NULL,
                    [BOOL_PROP_1] bit NULL,
                    [BOOL_PROP_2] bit NULL,
                    [TIME_ZONE_ID] nvarchar(80) NULL,
                    CONSTRAINT [PK_QRTZ_SIMPROP_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]),
                    CONSTRAINT [FK_QRTZ_SIMPROP_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP])
                        REFERENCES [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]) ON DELETE CASCADE
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_BLOB_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [BLOB_DATA] varbinary(max) NULL,
                    CONSTRAINT [PK_QRTZ_BLOB_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]),
                    CONSTRAINT [FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP])
                        REFERENCES [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]) ON DELETE CASCADE
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_FIRED_TRIGGERS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [ENTRY_ID] nvarchar(140) NOT NULL,
                    [TRIGGER_NAME] nvarchar(150) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    [INSTANCE_NAME] nvarchar(200) NOT NULL,
                    [FIRED_TIME] bigint NOT NULL,
                    [SCHED_TIME] bigint NOT NULL,
                    [PRIORITY] int NOT NULL,
                    [STATE] nvarchar(16) NOT NULL,
                    [JOB_NAME] nvarchar(150) NULL,
                    [JOB_GROUP] nvarchar(150) NULL,
                    [IS_NONCONCURRENT] bit NULL,
                    [REQUESTS_RECOVERY] bit NULL,
                    CONSTRAINT [PK_QRTZ_FIRED_TRIGGERS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [ENTRY_ID])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_PAUSED_TRIGGER_GRPS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [TRIGGER_GROUP] nvarchar(150) NOT NULL,
                    CONSTRAINT [PK_QRTZ_PAUSED_TRIGGER_GRPS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [TRIGGER_GROUP])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_SCHEDULER_STATE] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [INSTANCE_NAME] nvarchar(200) NOT NULL,
                    [LAST_CHECKIN_TIME] bigint NOT NULL,
                    [CHECKIN_INTERVAL] bigint NOT NULL,
                    CONSTRAINT [PK_QRTZ_SCHEDULER_STATE] PRIMARY KEY CLUSTERED ([SCHED_NAME], [INSTANCE_NAME])
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE [dbo].[QRTZ_LOCKS] (
                    [SCHED_NAME] nvarchar(120) NOT NULL,
                    [LOCK_NAME] nvarchar(40) NOT NULL,
                    CONSTRAINT [PK_QRTZ_LOCKS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [LOCK_NAME])
                );
                """
            );

            // Los índices del script oficial. El AdoJobStore barre por NEXT_FIRE_TIME + TRIGGER_STATE
            // en cada tick del scheduler; sin ellos ese barrido es un scan completo de la tabla.
            migrationBuilder.Sql(
                """
                CREATE INDEX [IDX_QRTZ_J_REQ_RECOVERY] ON [dbo].[QRTZ_JOB_DETAILS]([SCHED_NAME], [REQUESTS_RECOVERY]);
                CREATE INDEX [IDX_QRTZ_T_NEXT_FIRE_TIME] ON [dbo].[QRTZ_TRIGGERS]([SCHED_NAME], [NEXT_FIRE_TIME]);
                CREATE INDEX [IDX_QRTZ_T_STATE] ON [dbo].[QRTZ_TRIGGERS]([SCHED_NAME], [TRIGGER_STATE]);
                CREATE INDEX [IDX_QRTZ_T_NFT_ST] ON [dbo].[QRTZ_TRIGGERS]([SCHED_NAME], [NEXT_FIRE_TIME], [TRIGGER_STATE]);
                CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE] ON [dbo].[QRTZ_TRIGGERS]([SCHED_NAME], [MISFIRE_INSTR], [NEXT_FIRE_TIME], [TRIGGER_STATE]);
                CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE_GRP] ON [dbo].[QRTZ_TRIGGERS]([SCHED_NAME], [MISFIRE_INSTR], [NEXT_FIRE_TIME], [TRIGGER_GROUP], [TRIGGER_STATE]);
                CREATE INDEX [IDX_QRTZ_FT_TRIG_INST_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS]([SCHED_NAME], [INSTANCE_NAME]);
                CREATE INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY] ON [dbo].[QRTZ_FIRED_TRIGGERS]([SCHED_NAME], [INSTANCE_NAME], [REQUESTS_RECOVERY]);
                CREATE INDEX [IDX_QRTZ_FT_TRIG_NM_GP] ON [dbo].[QRTZ_FIRED_TRIGGERS]([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP]);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden inverso a las FKs: los subtipos de trigger antes que QRTZ_TRIGGERS, y éste antes
            // que QRTZ_JOB_DETAILS.
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS [dbo].[QRTZ_SIMPLE_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_CRON_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_SIMPROP_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_BLOB_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_JOB_DETAILS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_FIRED_TRIGGERS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_PAUSED_TRIGGER_GRPS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_SCHEDULER_STATE];
                DROP TABLE IF EXISTS [dbo].[QRTZ_LOCKS];
                DROP TABLE IF EXISTS [dbo].[QRTZ_CALENDARS];
                """
            );
        }
    }
}
