using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicBookingPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BookingPageId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "BookingPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TitleEs = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    DescriptionEs = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    AppointmentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MinNoticeMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxDaysAhead = table.Column<int>(type: "int", nullable: false),
                    BufferMinutes = table.Column<int>(type: "int", nullable: false),
                    SlotStepMinutes = table.Column<int>(type: "int", nullable: false),
                    RequireEmailVerification = table.Column<bool>(type: "bit", nullable: false),
                    ReminderHoursBefore = table.Column<int>(type: "int", nullable: true),
                    IcsAlarmMinutesJson = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneField = table.Column<int>(type: "int", nullable: false),
                    OfficeSizeField = table.Column<int>(type: "int", nullable: false),
                    InterestField = table.Column<int>(type: "int", nullable: false),
                    ConsentVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPages", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PublicBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    RequesterKey = table.Column<string>(type: "char(64)", nullable: true),
                    Company = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OfficeSize = table.Column<int>(type: "int", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Interest = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HeardAboutCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HeardAboutDetail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntrySource = table.Column<int>(type: "int", nullable: false),
                    UtmSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferrerDomain = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: true),
                    Language = table.Column<int>(type: "int", nullable: false),
                    VisitorTimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MarketingOptIn = table.Column<bool>(type: "bit", nullable: false),
                    ConsentTextVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SlotStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    HostUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHandleHash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    RequiresVerification = table.Column<bool>(type: "bit", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    CodeAttempts = table.Column<int>(type: "int", nullable: false),
                    CodeSends = table.Column<int>(type: "int", nullable: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HoldExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ManagementTokenHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    IcsSequence = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReminderSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<int>(type: "int", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutcomeAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutcomeByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnonymizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicBookings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "BookingPageHeardAboutOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LabelEs = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowsDetail = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPageHeardAboutOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPageHeardAboutOptions_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "BookingPageHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NotificationEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    MeetingUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPageHosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPageHosts_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookingPageId",
                table: "Appointments",
                column: "BookingPageId",
                filter: "[BookingPageId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_BookingPageHeardAboutOptions_BookingPageId_Code",
                table: "BookingPageHeardAboutOptions",
                columns: new[] { "BookingPageId", "Code" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_BookingPageHosts_BookingPageId_UserId",
                table: "BookingPageHosts",
                columns: new[] { "BookingPageId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_BookingPageHosts_UserId",
                table: "BookingPageHosts",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_BookingPages_TenantId_Slug",
                table: "BookingPages",
                columns: new[] { "TenantId", "Slug" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_AppointmentId",
                table: "PublicBookings",
                column: "AppointmentId",
                filter: "[AppointmentId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_ManagementTokenHash",
                table: "PublicBookings",
                column: "ManagementTokenHash",
                unique: true,
                filter: "[ManagementTokenHash] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_Requester_Holding",
                table: "PublicBookings",
                columns: new[] { "TenantId", "RequesterKey" },
                filter: "[Status] IN (0, 1)"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_RequestHandleHash",
                table: "PublicBookings",
                column: "RequestHandleHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_Status_HoldExpiresAtUtc",
                table: "PublicBookings",
                columns: new[] { "Status", "HoldExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_Status_SlotStartUtc",
                table: "PublicBookings",
                columns: new[] { "Status", "SlotStartUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_TenantId_BookingPageId_IdempotencyKey",
                table: "PublicBookings",
                columns: new[] { "TenantId", "BookingPageId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_PublicBookings_Host_Slot_Holding",
                table: "PublicBookings",
                columns: new[] { "TenantId", "HostUserId", "SlotStartUtc" },
                unique: true,
                filter: "[Status] IN (0, 1)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BookingPageHeardAboutOptions");

            migrationBuilder.DropTable(name: "BookingPageHosts");

            migrationBuilder.DropTable(name: "PublicBookings");

            migrationBuilder.DropTable(name: "BookingPages");

            migrationBuilder.DropIndex(name: "IX_Appointments_BookingPageId", table: "Appointments");

            migrationBuilder.DropColumn(name: "BookingPageId", table: "Appointments");
        }
    }
}
