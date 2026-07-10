using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignaturePlanConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Plan_AllowedChannels",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<long>(
                name: "Plan_MaxAllowedImageBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 10485760L);

            migrationBuilder.AddColumn<int>(
                name: "Plan_MaxAllowedPages",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<long>(
                name: "Plan_MaxAllowedPdfBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 26214400L);

            migrationBuilder.AddColumn<int>(
                name: "Plan_MaxTokenExpirationHours",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 720);

            migrationBuilder.AddColumn<int>(
                name: "Plan_MinRetentionYears",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "Plan_PurgeAllowed",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan_AllowedChannels",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxAllowedImageBytes",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxAllowedPages",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxAllowedPdfBytes",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxTokenExpirationHours",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MinRetentionYears",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_PurgeAllowed",
                table: "TenantSignatureSettings");
        }
    }
}
