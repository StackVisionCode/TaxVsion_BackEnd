using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharingPolicyAndStoragePlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultShareLifetimeDays",
                table: "TenantStorageLimits",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "MaxShareDownloads",
                table: "TenantStorageLimits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSharePasswordAttempts",
                table: "TenantStorageLimits",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "MinSharePasswordLength",
                table: "TenantStorageLimits",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "FailedPasswordAttempts",
                table: "ShareLinks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordLockedUntilUtc",
                table: "ShareLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoragePlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedUrlMinutes = table.Column<int>(type: "int", nullable: false),
                    ShareUrlMinutes = table.Column<int>(type: "int", nullable: false),
                    UploadReservationHours = table.Column<int>(type: "int", nullable: false),
                    RecycleBinRetentionDays = table.Column<int>(type: "int", nullable: false),
                    MaxZipFiles = table.Column<int>(type: "int", nullable: false),
                    MaxZipFolders = table.Column<int>(type: "int", nullable: false),
                    MaxZipAggregateBytes = table.Column<long>(type: "bigint", nullable: false),
                    SharePasswordLockMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoragePlatformSettings", x => x.Id);
                });

            // Los valores que tenían appsettings y el código (StoragePlatformSettings.Defaults()): sin cambio al desplegar.
            migrationBuilder.InsertData(
                table: "StoragePlatformSettings",
                columns: new[]
                {
                    "Id", "SignedUrlMinutes", "ShareUrlMinutes", "UploadReservationHours", "RecycleBinRetentionDays",
                    "MaxZipFiles", "MaxZipFolders", "MaxZipAggregateBytes", "SharePasswordLockMinutes",
                    "UpdatedByUserId", "UpdatedAtUtc",
                },
                values: new object[]
                {
                    new Guid("3f6d1d62-4c1b-4c84-9a1e-0b6c5f1a7e21"), 5, 2, 24, 30,
                    500, 50, 2L * 1024 * 1024 * 1024, 15,
                    Guid.Empty, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoragePlatformSettings");

            migrationBuilder.DropColumn(
                name: "DefaultShareLifetimeDays",
                table: "TenantStorageLimits");

            migrationBuilder.DropColumn(
                name: "MaxShareDownloads",
                table: "TenantStorageLimits");

            migrationBuilder.DropColumn(
                name: "MaxSharePasswordAttempts",
                table: "TenantStorageLimits");

            migrationBuilder.DropColumn(
                name: "MinSharePasswordLength",
                table: "TenantStorageLimits");

            migrationBuilder.DropColumn(
                name: "FailedPasswordAttempts",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "PasswordLockedUntilUtc",
                table: "ShareLinks");
        }
    }
}
