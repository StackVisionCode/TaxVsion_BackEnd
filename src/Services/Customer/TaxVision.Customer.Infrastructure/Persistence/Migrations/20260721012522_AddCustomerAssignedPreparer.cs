using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignedPreparer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedPreparerUserId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_AssignedPreparerUserId",
                table: "Customers",
                columns: new[] { "TenantId", "AssignedPreparerUserId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Customers_TenantId_AssignedPreparerUserId", table: "Customers");

            migrationBuilder.DropColumn(name: "AssignedPreparerUserId", table: "Customers");
        }
    }
}
