using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingLegalHoldAndUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderSentAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "SignatureRequests",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldLiftedAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LegalHoldLiftedByUserId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldPlacedAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "LegalHoldPlacedByUserId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "SignatureRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RemindersSent",
                table: "SignatureRequests",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "UserPermissionsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionsVersion = table.Column<int>(type: "int", nullable: false),
                    RolesCsv = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionsProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId_LegalHold",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "LegalHold" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                table: "UserPermissionsProjections",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserPermissionsProjections");

            migrationBuilder.DropIndex(name: "IX_SignatureRequests_TenantId_LegalHold", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LastReminderSentAtUtc", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHold", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHoldLiftedAtUtc", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHoldLiftedByUserId", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHoldPlacedAtUtc", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHoldPlacedByUserId", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "LegalHoldReason", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "RemindersSent", table: "SignatureRequests");
        }
    }
}
