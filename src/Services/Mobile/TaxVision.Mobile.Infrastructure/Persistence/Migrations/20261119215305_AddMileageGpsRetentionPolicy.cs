using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Mobile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMileageGpsRetentionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CoordinatesPurgedAtUtc",
                table: "MobileTrips",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MobileMileageRetentionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileMileageRetentionPolicies", x => x.Id);
                    table.CheckConstraint("CK_MobileMileageRetentionPolicies_Days", "[RetentionDays] IS NULL OR [RetentionDays] BETWEEN 1 AND 3650");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobileMileageRetentionPolicies_TenantId",
                table: "MobileMileageRetentionPolicies",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileMileageRetentionPolicies");

            migrationBuilder.DropColumn(
                name: "CoordinatesPurgedAtUtc",
                table: "MobileTrips");
        }
    }
}
