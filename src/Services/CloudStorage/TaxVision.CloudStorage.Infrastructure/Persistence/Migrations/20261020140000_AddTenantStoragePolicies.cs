using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CloudStorageDbContext))]
[Migration("20261020140000_AddTenantStoragePolicies")]
public sealed class AddTenantStoragePolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantStoragePolicies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProviderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "crm"),
                BackupFrequency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                BackupTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                BackupTimeZoneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                LastBackupAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_TenantStoragePolicies", x => x.Id));

        migrationBuilder.Sql("""
            INSERT INTO TenantStoragePolicies (Id, TenantId, ProviderId, Mode, BackupFrequency, BackupTime, BackupTimeZoneId, LastBackupAtUtc)
            SELECT Id, TenantId, 'google-drive', GoogleDriveMode, BackupFrequency, BackupTime, BackupTimeZoneId, LastBackupAtUtc
            FROM TenantStorageLimits
            WHERE GoogleDriveMode <> 'crm' OR StorageProvider = 'google-drive'
            """);

        migrationBuilder.CreateIndex(
            name: "IX_TenantStoragePolicies_TenantId",
            table: "TenantStoragePolicies",
            column: "TenantId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "TenantStoragePolicies");
}
