using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefereeTenantIdToReferralAttributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RefereeTenantId",
                schema: "referrals",
                table: "ReferralAttributions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ReferralAttributions_RefereeTenantId",
                schema: "referrals",
                table: "ReferralAttributions",
                column: "RefereeTenantId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReferralAttributions_RefereeTenantId",
                schema: "referrals",
                table: "ReferralAttributions"
            );

            migrationBuilder.DropColumn(name: "RefereeTenantId", schema: "referrals", table: "ReferralAttributions");
        }
    }
}
