using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureAnalyticsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestsCreated = table.Column<int>(type: "int", nullable: false),
                    RequestsSent = table.Column<int>(type: "int", nullable: false),
                    RequestsCanceled = table.Column<int>(type: "int", nullable: false),
                    RequestsExpired = table.Column<int>(type: "int", nullable: false),
                    RequestsCompleted = table.Column<int>(type: "int", nullable: false),
                    RequestsSealed = table.Column<int>(type: "int", nullable: false),
                    SignersSigned = table.Column<int>(type: "int", nullable: false),
                    SignersRejected = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAnalyticsSnapshots", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAnalyticsSnapshots_TenantId_Day_Category",
                table: "SignatureAnalyticsSnapshots",
                columns: new[] { "TenantId", "Day", "Category" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SignatureAnalyticsSnapshots");
        }
    }
}
