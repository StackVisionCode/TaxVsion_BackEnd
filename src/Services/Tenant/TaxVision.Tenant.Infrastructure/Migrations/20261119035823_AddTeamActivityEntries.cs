using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamActivityEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Verb = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CustomerAssignmentProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignmentProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_OccurredAtUtc",
                table: "ActivityEntries",
                column: "OccurredAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_TenantId_ActorUserId_OccurredAtUtc",
                table: "ActivityEntries",
                columns: new[] { "TenantId", "ActorUserId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_TenantId_EventId",
                table: "ActivityEntries",
                columns: new[] { "TenantId", "EventId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId_UserId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActivityEntries");

            migrationBuilder.DropTable(name: "CustomerAssignmentProjections");
        }
    }
}
