using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormsRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetentionYearsOverride",
                table: "FormModuleSettings",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "FormRetentionTombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rule = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FinalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentRevisionNumber = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    PurgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormRetentionTombstones", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormRetentionTombstones_TenantId_FormRequestId",
                table: "FormRetentionTombstones",
                columns: new[] { "TenantId", "FormRequestId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormRetentionTombstones");

            migrationBuilder.DropColumn(name: "RetentionYearsOverride", table: "FormModuleSettings");
        }
    }
}
