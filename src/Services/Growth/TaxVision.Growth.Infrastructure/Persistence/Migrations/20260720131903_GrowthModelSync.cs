using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrowthModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MinimumPaymentCurrency",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "char(3)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "char(3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MinimumPaymentCurrency",
                schema: "referrals",
                table: "ReferralPrograms",
                type: "char(3)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "char(3)",
                oldNullable: true);
        }
    }
}
