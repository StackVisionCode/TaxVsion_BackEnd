using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilesTenantCreatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder
                .CreateIndex(
                    name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                    table: "CustomerAssignmentProjections",
                    columns: new[] { "TenantId", "UserId" }
                )
                .Annotation("SqlServer:Include", new[] { "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Files_Tenant_Created", table: "Files");

            migrationBuilder.DropIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                table: "CustomerAssignmentProjections"
            );
        }
    }
}
