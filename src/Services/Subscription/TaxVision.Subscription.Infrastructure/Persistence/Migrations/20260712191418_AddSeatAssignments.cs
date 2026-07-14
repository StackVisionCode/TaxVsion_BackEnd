using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentAssignmentId",
                table: "SubscriptionSeats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentUserId",
                table: "SubscriptionSeats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionSeatAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSeatAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSeatAssignments_SubscriptionSeats_SeatId",
                        column: x => x.SeatId,
                        principalTable: "SubscriptionSeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSeats_CurrentUserId",
                table: "SubscriptionSeats",
                column: "CurrentUserId",
                filter: "[CurrentUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSeatAssignments_UserId_TenantId",
                table: "SubscriptionSeatAssignments",
                columns: new[] { "UserId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_SubscriptionSeatAssignments_SeatId_Active",
                table: "SubscriptionSeatAssignments",
                column: "SeatId",
                unique: true,
                filter: "[ReleasedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionSeatAssignments");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionSeats_CurrentUserId",
                table: "SubscriptionSeats");

            migrationBuilder.DropColumn(
                name: "CurrentAssignmentId",
                table: "SubscriptionSeats");

            migrationBuilder.DropColumn(
                name: "CurrentUserId",
                table: "SubscriptionSeats");
        }
    }
}
