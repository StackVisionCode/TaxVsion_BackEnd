using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeEmailThreadProviderThreadIndexByCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailThreads_TenantId_ProviderThreadId_Unique",
                table: "EmailThreads");

            migrationBuilder.CreateIndex(
                name: "IX_EmailThreads_TenantId_CustomerId_ProviderThreadId_Unique",
                table: "EmailThreads",
                columns: new[] { "TenantId", "CustomerId", "ProviderThreadId" },
                unique: true,
                filter: "[ProviderThreadId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailThreads_TenantId_CustomerId_ProviderThreadId_Unique",
                table: "EmailThreads");

            migrationBuilder.CreateIndex(
                name: "IX_EmailThreads_TenantId_ProviderThreadId_Unique",
                table: "EmailThreads",
                columns: new[] { "TenantId", "ProviderThreadId" },
                unique: true,
                filter: "[ProviderThreadId] IS NOT NULL");
        }
    }
}
