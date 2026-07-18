using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrespondenceAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrespondenceAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondenceAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondenceAuditLogs_TenantId_TargetId",
                table: "CorrespondenceAuditLogs",
                columns: new[] { "TenantId", "TargetId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CorrespondenceAuditLogs");
        }
    }
}
