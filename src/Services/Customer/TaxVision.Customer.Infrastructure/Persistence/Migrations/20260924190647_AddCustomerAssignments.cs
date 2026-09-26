using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAssignments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_CustomerId",
                table: "CustomerAssignments",
                column: "CustomerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_TenantId_CustomerId_UserId",
                table: "CustomerAssignments",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignments_TenantId_UserId",
                table: "CustomerAssignments",
                columns: new[] { "TenantId", "UserId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAssignments");
        }
    }
}
