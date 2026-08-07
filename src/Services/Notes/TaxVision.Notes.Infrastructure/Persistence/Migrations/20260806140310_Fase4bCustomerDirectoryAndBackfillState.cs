using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fase4bCustomerDirectoryAndBackfillState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerDirectoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDirectoryEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantBackfillStates",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBackfillStates", x => x.TenantId);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDirectoryEntries_TenantId_CustomerId",
                table: "CustomerDirectoryEntries",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDirectoryEntries_TenantId_DisplayName",
                table: "CustomerDirectoryEntries",
                columns: new[] { "TenantId", "DisplayName" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerDirectoryEntries");

            migrationBuilder.DropTable(name: "TenantBackfillStates");
        }
    }
}
