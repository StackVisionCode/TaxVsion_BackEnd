using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginSourceReferenceId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "OriginSourceService",
                table: "Customers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_Customers_Tenant_Origin",
                table: "Customers",
                columns: new[] { "TenantId", "OriginSourceService", "OriginSourceReferenceId" },
                unique: true,
                filter: "[OriginSourceReferenceId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_Customers_Tenant_Origin", table: "Customers");

            migrationBuilder.DropColumn(name: "OriginSourceReferenceId", table: "Customers");

            migrationBuilder.DropColumn(name: "OriginSourceService", table: "Customers");
        }
    }
}
