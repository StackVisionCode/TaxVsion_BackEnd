using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacedScheduleToPlanChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedCancellationAtUtc",
                table: "PlanChangeRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReplacedCancellationReason",
                table: "PlanChangeRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedDowngradeId",
                table: "PlanChangeRequests",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReplacedCancellationAtUtc", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "ReplacedCancellationReason", table: "PlanChangeRequests");

            migrationBuilder.DropColumn(name: "ReplacedDowngradeId", table: "PlanChangeRequests");
        }
    }
}
