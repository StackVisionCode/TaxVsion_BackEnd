using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaxVision.CloudStorage.Infrastructure.Persistence;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CloudStorageDbContext))]
[Migration("20261020120000_AddGoogleDriveUsagePolicy")]
public partial class AddGoogleDriveUsagePolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("GoogleDriveMode", "TenantStorageLimits", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "crm");
        migrationBuilder.AddColumn<string>("BackupFrequency", "TenantStorageLimits", type: "nvarchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>("BackupTime", "TenantStorageLimits", type: "nvarchar(5)", maxLength: 5, nullable: true);
        migrationBuilder.AddColumn<string>("BackupTimeZoneId", "TenantStorageLimits", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTime>("LastBackupAtUtc", "TenantStorageLimits", type: "datetime2", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("GoogleDriveMode", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupFrequency", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupTime", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupTimeZoneId", "TenantStorageLimits");
        migrationBuilder.DropColumn("LastBackupAtUtc", "TenantStorageLimits");
    }
}
