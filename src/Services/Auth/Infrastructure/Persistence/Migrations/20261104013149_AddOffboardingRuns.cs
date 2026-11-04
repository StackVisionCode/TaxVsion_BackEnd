using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OffboardingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IncompleteAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingRuns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "OffboardingRunSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Service = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Step = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingRunSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OffboardingRunSteps_OffboardingRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "OffboardingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingRuns_IncompleteAtUtc_StartedAtUtc",
                table: "OffboardingRuns",
                columns: new[] { "IncompleteAtUtc", "StartedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingRuns_TenantId_UserId",
                table: "OffboardingRuns",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingRunSteps_RunId_Service_Step",
                table: "OffboardingRunSteps",
                columns: new[] { "RunId", "Service", "Step" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OffboardingRunSteps");

            migrationBuilder.DropTable(name: "OffboardingRuns");
        }
    }
}
