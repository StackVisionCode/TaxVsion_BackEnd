using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeatPricings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatPricings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeatPriceTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeatPricingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeatType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatPriceTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatPriceTiers_SeatPricings_SeatPricingId",
                        column: x => x.SeatPricingId,
                        principalTable: "SeatPricings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeatPriceTiers_SeatPricingId_SeatType_BillingCycle",
                table: "SeatPriceTiers",
                columns: new[] { "SeatPricingId", "SeatType", "BillingCycle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeatPriceTiers");

            migrationBuilder.DropTable(
                name: "SeatPricings");
        }
    }
}
