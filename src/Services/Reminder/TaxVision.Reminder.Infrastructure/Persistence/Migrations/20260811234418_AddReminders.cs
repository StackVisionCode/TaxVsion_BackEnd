using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Reminder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FireAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AnchorAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeadMinutes = table.Column<int>(type: "int", nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SnoozeCount = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_Category_TargetId",
                table: "Reminders",
                columns: new[] { "Category", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "UX_Reminders_TenantId_RequestKey",
                table: "Reminders",
                columns: new[] { "TenantId", "RequestKey" },
                unique: true);

            // Los dos índices que siguen no se pueden declarar en ReminderConfiguration: cruzan
            // columnas de la raíz (TenantId/UserId/Status) con FireAtUtc, que pertenece al owned
            // type Schedule. Medido en EF Core 10 — HasIndex rechaza la ruta anidada con "not a
            // valid member access expression" aunque ambos entity types compartan tabla. En SQL
            // las columnas son columnas normales de Reminders, así que el índice físico sí existe;
            // lo único que no existe es su declaración en el modelo de EF (y por eso tampoco
            // aparecen en el ModelSnapshot: EF no intentará borrarlos en migraciones futuras).
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Reminders_TenantId_UserId_Status_FireAtUtc
                    ON Reminders (TenantId, UserId, Status, FireAtUtc);
                """
            );

            // Cross-tenant, sin TenantId a propósito: lo consume el job de reconciliación de la
            // Fase 5, que barre TODOS los tenants buscando recordatorios agendados sin trigger
            // vivo en Quartz. Sin este índice ese barrido es un scan completo de la tabla.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Reminders_Status_FireAtUtc
                    ON Reminders (Status, FireAtUtc);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reminders");
        }
    }
}
