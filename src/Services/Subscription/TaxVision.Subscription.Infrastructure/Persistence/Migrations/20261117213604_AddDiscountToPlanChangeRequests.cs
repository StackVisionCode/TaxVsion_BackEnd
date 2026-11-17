using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountToPlanChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiscountAmountCents",
                table: "PlanChangeRequests",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DiscountLabel",
                table: "PlanChangeRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DiscountSource",
                table: "PlanChangeRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DiscountAmountCents", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "DiscountLabel", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "DiscountSource", table: "PlanChangeRequests");
        }
    }
}
