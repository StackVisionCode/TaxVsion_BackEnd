using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEntitlementOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantEntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryAppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEntitlementOverrides", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlementOverrides_ExpiresAtUtc_Pending",
                table: "TenantEntitlementOverrides",
                column: "ExpiresAtUtc",
                filter: "[ExpiresAtUtc] IS NOT NULL AND [ExpiryAppliedAtUtc] IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantEntitlementOverrides_TenantId_Key",
                table: "TenantEntitlementOverrides",
                columns: new[] { "TenantId", "Key" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantEntitlementOverrides");
        }
    }
}
