using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TuneDashboardSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Files_Tenant_Created", table: "Files");

            migrationBuilder.DropIndex(name: "IX_Files_TenantId_Status_CreatedAtUtc", table: "Files");

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Tenant_Created",
                    table: "Files",
                    columns: new[] { "TenantId", "CreatedAtUtc" },
                    descending: new[] { false, true }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[] { "Status", "OwnerType", "OwnerId", "FolderType", "SourceResourceType", "OriginalName" }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_TenantId_Status_CreatedAtUtc",
                    table: "Files",
                    columns: new[] { "TenantId", "Status", "CreatedAtUtc" }
                )
                .Annotation("SqlServer:Include", new[] { "OwnerType", "OwnerId", "FolderType", "SourceResourceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Files_Tenant_Created", table: "Files");

            migrationBuilder.DropIndex(name: "IX_Files_TenantId_Status_CreatedAtUtc", table: "Files");

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Tenant_Created",
                    table: "Files",
                    columns: new[] { "TenantId", "CreatedAtUtc" },
                    descending: new[] { false, true }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[] { "Status", "OwnerType", "OwnerId", "FolderType", "SourceResourceType" }
                );

            migrationBuilder.CreateIndex(
                name: "IX_Files_TenantId_Status_CreatedAtUtc",
                table: "Files",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" }
            );
        }
    }
}
