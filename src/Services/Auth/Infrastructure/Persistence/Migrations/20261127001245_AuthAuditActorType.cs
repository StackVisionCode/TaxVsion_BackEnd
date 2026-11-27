using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthAuditActorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                table: "AuthAuditLogs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLogs_TenantId_TargetId_OccurredAtUtc",
                table: "AuthAuditLogs",
                columns: new[] { "TenantId", "TargetId", "OccurredAtUtc" },
                filter: "[TargetId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthAuditLogs_TenantId_TargetId_OccurredAtUtc",
                table: "AuthAuditLogs"
            );

            migrationBuilder.DropColumn(name: "ActorType", table: "AuthAuditLogs");
        }
    }
}
