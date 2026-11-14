using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchSourceCompletionAssignmentsAndQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentsPerMonth",
                table: "TenantEntitlementsProjections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionNotifiedAtUtc",
                table: "ImportBatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ImportBatches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                // Los lotes previos los registró el staff (el portal no tenía este campo).
                defaultValue: "Staff");

            migrationBuilder.CreateTable(
                name: "CustomerAssignmentProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignmentProjections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId_UserId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAssignmentProjections");

            migrationBuilder.DropColumn(
                name: "DocumentsPerMonth",
                table: "TenantEntitlementsProjections");

            migrationBuilder.DropColumn(
                name: "CompletionNotifiedAtUtc",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ImportBatches");
        }
    }
}
