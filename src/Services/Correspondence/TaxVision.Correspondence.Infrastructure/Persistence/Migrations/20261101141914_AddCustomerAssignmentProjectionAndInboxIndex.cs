using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAssignmentProjectionAndInboxIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAssignmentProjections",
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
                name: "IX_EmailThreads_TenantId_Status_LastMessageAtUtc",
                table: "EmailThreads",
                columns: new[] { "TenantId", "Status", "LastMessageAtUtc" },
                descending: new[] { false, false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId_UserId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAssignmentProjections");

            migrationBuilder.DropIndex(name: "IX_EmailThreads_TenantId_Status_LastMessageAtUtc", table: "EmailThreads");
        }
    }
}
