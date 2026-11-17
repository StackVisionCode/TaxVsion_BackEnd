using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyerToCodeQuotesAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerSubjectId",
                schema: "codes",
                table: "CodeReservations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BuyerSubjectId",
                schema: "codes",
                table: "CodeQuotes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BuyerSubjectId", schema: "codes", table: "CodeReservations");

            migrationBuilder.DropColumn(name: "BuyerSubjectId", schema: "codes", table: "CodeQuotes");
        }
    }
}
