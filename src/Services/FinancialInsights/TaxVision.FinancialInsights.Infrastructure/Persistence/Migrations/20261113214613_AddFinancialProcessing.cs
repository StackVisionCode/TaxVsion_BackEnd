using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProcessingReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProcessingReservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    SuggestedCategory = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SourcePage = table.Column<int>(type: "int", nullable: false),
                    RequiresReview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialMovements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProcessingReservations_TenantId_ImportDocumentId",
                table: "AiProcessingReservations",
                columns: new[] { "TenantId", "ImportDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProcessingReservations_TenantId_MonthStartUtc",
                table: "AiProcessingReservations",
                columns: new[] { "TenantId", "MonthStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMovements_TenantId_CustomerId_TransactionDate",
                table: "FinancialMovements",
                columns: new[] { "TenantId", "CustomerId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMovements_TenantId_ImportDocumentId",
                table: "FinancialMovements",
                columns: new[] { "TenantId", "ImportDocumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProcessingReservations");

            migrationBuilder.DropTable(
                name: "FinancialMovements");
        }
    }
}
