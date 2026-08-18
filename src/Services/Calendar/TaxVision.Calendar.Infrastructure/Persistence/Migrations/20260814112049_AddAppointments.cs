using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppointmentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimingKind = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LocalStartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    SeriesStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                    RecurrenceRule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SplitFromSeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxYear = table.Column<int>(type: "int", nullable: true),
                    IsVirtual = table.Column<bool>(type: "bit", nullable: false),
                    MeetingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MeetingShortCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "AppointmentAttendees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Response = table.Column<int>(type: "int", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentAttendees_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "AppointmentExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    NewStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewLocation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentExceptions_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAttendees_AppointmentId",
                table: "AppointmentAttendees",
                column: "AppointmentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentAttendees_UserId_AppointmentId",
                table: "AppointmentAttendees",
                columns: new[] { "UserId", "AppointmentId" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_AppointmentExceptions_AppointmentId_OriginalStartUtc",
                table: "AppointmentExceptions",
                columns: new[] { "AppointmentId", "OriginalStartUtc" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_CustomerId_TaxYear",
                table: "Appointments",
                columns: new[] { "TenantId", "CustomerId", "TaxYear" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_OrganizerUserId_Status",
                table: "Appointments",
                columns: new[] { "TenantId", "OrganizerUserId", "Status" }
            );
            // Los dos indices de rango mezclan columnas del owned type EventTiming (StartUtc, EndUtc)
            // con columnas de la raiz (TenantId, RecurrenceRule). HasIndex no cruza entity types
            // —aunque compartan tabla—, asi que van en SQL. No aparecen en el ModelSnapshot: la unica
            // forma de comprobar que existen es consultar sys.indexes.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Appointments_TenantId_StartUtc_EndUtc_OneOff
                    ON Appointments (TenantId, StartUtc, EndUtc)
                    WHERE RecurrenceRule IS NULL;
                """
            );

            // Las series se expanden TODAS: no hay indice que ayude a filtrarlas por fecha, porque su
            // StartUtc es NULL por diseno. Este solo evita recorrer las puntuales, que son el 90%.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_Appointments_TenantId_Series
                    ON Appointments (TenantId)
                    WHERE RecurrenceRule IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Appointments_TenantId_StartUtc_EndUtc_OneOff ON Appointments;"
            );
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Appointments_TenantId_Series ON Appointments;");
            migrationBuilder.DropTable(name: "AppointmentAttendees");

            migrationBuilder.DropTable(name: "AppointmentExceptions");

            migrationBuilder.DropTable(name: "Appointments");
        }
    }
}
