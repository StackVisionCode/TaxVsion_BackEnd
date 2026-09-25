using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignmentVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            // Backfill de CustomerId desde el snapshot JSON de las facturas existentes (P2). COALESCE cubre
            // ambas casings del JSON; TRY_CAST descarta valores no-guid (esas quedan en Guid.Empty = admin-only).
            migrationBuilder.Sql(
                """
                UPDATE [billing].[Invoices]
                SET [CustomerId] = TRY_CAST(COALESCE(
                        JSON_VALUE([Customer], '$.CustomerId'),
                        JSON_VALUE([Customer], '$.customerId')) AS uniqueidentifier)
                WHERE [Customer] IS NOT NULL
                  AND ISJSON([Customer]) = 1
                  AND TRY_CAST(COALESCE(
                        JSON_VALUE([Customer], '$.CustomerId'),
                        JSON_VALUE([Customer], '$.customerId')) AS uniqueidentifier) IS NOT NULL;
                """
            );

            migrationBuilder.CreateTable(
                name: "CustomerAssignmentProjections",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignmentProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_CustomerId",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "CustomerId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId",
                schema: "billing",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId_UserId",
                schema: "billing",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAssignmentProjections", schema: "billing");

            migrationBuilder.DropIndex(name: "IX_Invoices_TenantId_CustomerId", schema: "billing", table: "Invoices");

            migrationBuilder.DropColumn(name: "CustomerId", schema: "billing", table: "Invoices");
        }
    }
}
