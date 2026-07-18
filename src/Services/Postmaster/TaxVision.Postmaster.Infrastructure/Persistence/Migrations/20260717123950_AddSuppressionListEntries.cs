using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppressionListEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuppressionListEntries",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuppressionListEntries", x => new { x.TenantId, x.EmailAddress });
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SuppressionListEntries_TenantId_Reason_AddedAtUtc",
                table: "SuppressionListEntries",
                columns: new[] { "TenantId", "Reason", "AddedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SuppressionListEntries");
        }
    }
}
