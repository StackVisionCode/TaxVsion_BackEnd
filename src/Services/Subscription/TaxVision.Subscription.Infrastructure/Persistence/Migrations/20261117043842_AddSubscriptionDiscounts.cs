using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionDiscounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PercentBasisPoints = table.Column<int>(type: "int", nullable: true),
                    AmountCents = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Duration = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Months = table.Column<int>(type: "int", nullable: true),
                    AppliesTo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionDiscounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionDiscounts_TenantSubscriptions_TenantSubscriptionId",
                        column: x => x.TenantSubscriptionId,
                        principalTable: "TenantSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDiscounts_EndsAtUtc_Open",
                table: "SubscriptionDiscounts",
                column: "EndsAtUtc",
                filter: "[EndedAtUtc] IS NULL AND [EndsAtUtc] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDiscounts_TenantSubscriptionId_EndedAtUtc",
                table: "SubscriptionDiscounts",
                columns: new[] { "TenantSubscriptionId", "EndedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SubscriptionDiscounts");
        }
    }
}
