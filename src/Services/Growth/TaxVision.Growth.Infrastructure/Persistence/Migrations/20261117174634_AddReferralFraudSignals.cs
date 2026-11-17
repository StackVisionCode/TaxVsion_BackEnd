using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralFraudSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OwnerEmailDisposable",
                schema: "referrals",
                table: "ReferredOffices",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "OwnerSubjectId",
                schema: "referrals",
                table: "ReferredOffices",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RefereeUserId",
                schema: "referrals",
                table: "ReferralAttributions",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OwnerEmailDisposable", schema: "referrals", table: "ReferredOffices");

            migrationBuilder.DropColumn(name: "OwnerSubjectId", schema: "referrals", table: "ReferredOffices");

            migrationBuilder.DropColumn(name: "RefereeUserId", schema: "referrals", table: "ReferralAttributions");
        }
    }
}
