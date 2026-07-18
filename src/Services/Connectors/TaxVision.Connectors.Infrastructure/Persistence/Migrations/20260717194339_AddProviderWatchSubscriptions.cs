using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderWatchSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderWatchSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubscriptionRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TopicName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRenewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderWatchSubscriptions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProviderWatchSubscriptions_AccountId",
                table: "ProviderWatchSubscriptions",
                column: "AccountId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProviderWatchSubscriptions_ExpiresAtUtc",
                table: "ProviderWatchSubscriptions",
                column: "ExpiresAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProviderWatchSubscriptions");
        }
    }
}
