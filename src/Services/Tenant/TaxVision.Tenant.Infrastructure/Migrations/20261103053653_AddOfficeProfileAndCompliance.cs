using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeProfileAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EfinRevealRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevealedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevealedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EfinRevealRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "OfficeCompliances",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EfinCiphertext = table.Column<byte[]>(type: "varbinary(64)", maxLength: 64, nullable: true),
                    EfinNonce = table.Column<byte[]>(type: "varbinary(12)", maxLength: 12, nullable: true),
                    EfinTag = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: true),
                    EfinKeyVersion = table.Column<short>(type: "smallint", nullable: true),
                    EfinLastDigits = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    SecurityCoordinatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WispAdoptedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    WispLastReviewedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IncidentContactEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    IncidentContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RetentionYears = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeCompliances", x => x.TenantId);
                }
            );

            migrationBuilder.CreateTable(
                name: "OfficeProfiles",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Ein = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DefaultLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BookingUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ReviewUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BusinessHoursJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MailingAddressJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhysicalAddressJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeProfiles", x => x.TenantId);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EfinRevealRecords_TenantId_RevealedAtUtc",
                table: "EfinRevealRecords",
                columns: new[] { "TenantId", "RevealedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EfinRevealRecords");

            migrationBuilder.DropTable(name: "OfficeCompliances");

            migrationBuilder.DropTable(name: "OfficeProfiles");
        }
    }
}
