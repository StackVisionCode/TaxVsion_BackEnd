using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriberMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScheduledByMigrationId",
                table: "TenantSubscriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledVersionAtUtc",
                table: "TenantSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduledVersionId",
                table: "TenantSubscriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriberMigrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToVersionNumber = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NoticeDays = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberMigrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriberMigrationOffices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MigrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberMigrationOffices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriberMigrationOffices_SubscriberMigrations_MigrationId",
                        column: x => x.MigrationId,
                        principalTable: "SubscriberMigrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberMigrationOffices_MigrationId_TenantId",
                table: "SubscriberMigrationOffices",
                columns: new[] { "MigrationId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberMigrations_Status_CreatedAtUtc",
                table: "SubscriberMigrations",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriberMigrationOffices");

            migrationBuilder.DropTable(
                name: "SubscriberMigrations");

            migrationBuilder.DropColumn(
                name: "ScheduledByMigrationId",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "ScheduledVersionAtUtc",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "ScheduledVersionId",
                table: "TenantSubscriptions");
        }
    }
}
