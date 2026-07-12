using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerImportActiveJobUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_CustomerImportAttempts_Tenant_Active",
                table: "CustomerImportAttempts",
                column: "TenantId",
                unique: true,
                filter: "[Status] IN ('Queued','Validating','Applying','Canceling')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CustomerImportAttempts_Tenant_Active",
                table: "CustomerImportAttempts"
            );
        }
    }
}
