using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicBookingConsentAndTokenVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_PublicBookings_Requester_Holding", table: "PublicBookings");

            migrationBuilder.AddColumn<int>(
                name: "ManagementTokenVersion",
                table: "PublicBookings",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionEs",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionEn",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600
            );

            migrationBuilder.AddColumn<string>(
                name: "MarketingConsentEn",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "MarketingConsentEs",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PrivacyNoticeEn",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PrivacyNoticeEs",
                table: "BookingPages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_PublicBookings_Requester_Holding",
                table: "PublicBookings",
                columns: new[] { "TenantId", "BookingPageId", "RequesterKey" },
                unique: true,
                filter: "[Status] IN (0, 1)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_PublicBookings_Requester_Holding", table: "PublicBookings");

            migrationBuilder.DropColumn(name: "ManagementTokenVersion", table: "PublicBookings");

            migrationBuilder.DropColumn(name: "MarketingConsentEn", table: "BookingPages");

            migrationBuilder.DropColumn(name: "MarketingConsentEs", table: "BookingPages");

            migrationBuilder.DropColumn(name: "PrivacyNoticeEn", table: "BookingPages");

            migrationBuilder.DropColumn(name: "PrivacyNoticeEs", table: "BookingPages");

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionEs",
                table: "BookingPages",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionEn",
                table: "BookingPages",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000
            );

            migrationBuilder.CreateIndex(
                name: "IX_PublicBookings_Requester_Holding",
                table: "PublicBookings",
                columns: new[] { "TenantId", "RequesterKey" },
                filter: "[Status] IN (0, 1)"
            );
        }
    }
}
