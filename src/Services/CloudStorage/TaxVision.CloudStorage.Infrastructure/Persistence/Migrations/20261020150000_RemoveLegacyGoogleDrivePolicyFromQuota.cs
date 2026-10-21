using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CloudStorageDbContext))]
[Migration("20261020150000_RemoveLegacyGoogleDrivePolicyFromQuota")]
public sealed class RemoveLegacyGoogleDrivePolicyFromQuota : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("GoogleDriveMode", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupFrequency", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupTime", "TenantStorageLimits");
        migrationBuilder.DropColumn("BackupTimeZoneId", "TenantStorageLimits");
        migrationBuilder.DropColumn("LastBackupAtUtc", "TenantStorageLimits");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("GoogleDriveMode", "TenantStorageLimits", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "crm");
        migrationBuilder.AddColumn<string>("BackupFrequency", "TenantStorageLimits", type: "nvarchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>("BackupTime", "TenantStorageLimits", type: "nvarchar(5)", maxLength: 5, nullable: true);
        migrationBuilder.AddColumn<string>("BackupTimeZoneId", "TenantStorageLimits", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTime>("LastBackupAtUtc", "TenantStorageLimits", type: "datetime2", nullable: true);
        migrationBuilder.Sql("""
            UPDATE limits
            SET limits.GoogleDriveMode = policies.Mode,
                limits.BackupFrequency = policies.BackupFrequency,
                limits.BackupTime = policies.BackupTime,
                limits.BackupTimeZoneId = policies.BackupTimeZoneId,
                limits.LastBackupAtUtc = policies.LastBackupAtUtc,
                limits.StorageProvider = CASE WHEN policies.Mode = 'primary' THEN policies.ProviderId ELSE 'internal' END
            FROM TenantStorageLimits AS limits
            INNER JOIN TenantStoragePolicies AS policies ON policies.TenantId = limits.TenantId
            """);
    }
}
