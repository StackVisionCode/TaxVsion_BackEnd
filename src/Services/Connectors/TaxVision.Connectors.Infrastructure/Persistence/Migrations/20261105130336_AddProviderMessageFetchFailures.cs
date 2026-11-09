using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderMessageFetchFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderMessageFetchFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    FirstFailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastFailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderMessageFetchFailures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderMessageFetchFailures_AccountId_ProviderMessageId",
                table: "ProviderMessageFetchFailures",
                columns: new[] { "AccountId", "ProviderMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderMessageFetchFailures");
        }
    }
}
