using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAuditLogs_TenantId_ActorUserId_OccurredAtUtc",
                table: "CustomerAuditLogs",
                columns: new[] { "TenantId", "ActorUserId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAuditLogs_TenantId_CustomerId_OccurredAtUtc",
                table: "CustomerAuditLogs",
                columns: new[] { "TenantId", "CustomerId", "OccurredAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAuditLogs");
        }
    }
}
