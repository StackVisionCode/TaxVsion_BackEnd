using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingReceiptLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingReceiptLookups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptDownloadUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingReceiptLookups", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingReceiptLookups_OnboardingId",
                table: "OnboardingReceiptLookups",
                column: "OnboardingId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OnboardingReceiptLookups");
        }
    }
}
