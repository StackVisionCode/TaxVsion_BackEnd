using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TuneDashboardSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
            migrationBuilder.DropIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_UserId",
                table: "CustomerAssignmentProjections"
            );
        }
    }
}
