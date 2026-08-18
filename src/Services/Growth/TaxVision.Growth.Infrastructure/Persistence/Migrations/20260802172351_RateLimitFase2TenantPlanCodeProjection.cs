using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RateLimitFase2TenantPlanCodeProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "ratelimiting");

            migrationBuilder.CreateTable(
                name: "TenantPlanCodeProjections",
                schema: "ratelimiting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPlanCodeProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPlanCodeProjections_TenantId",
                schema: "ratelimiting",
                table: "TenantPlanCodeProjections",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantPlanCodeProjections", schema: "ratelimiting");
        }
    }
}
