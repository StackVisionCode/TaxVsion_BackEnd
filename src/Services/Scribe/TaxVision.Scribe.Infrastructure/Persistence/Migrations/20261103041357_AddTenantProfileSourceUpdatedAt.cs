using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProfileSourceUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sello de Tenant del último renombre aplicado (ordena TenantUpdatedIntegrationEvent).
            migrationBuilder.AddColumn<DateTime>(
                name: "SourceUpdatedAtUtc",
                table: "TenantProfileRefs",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SourceUpdatedAtUtc", table: "TenantProfileRefs");
        }
    }
}
