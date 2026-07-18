using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingEmailsThreadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_TenantId_EmailThreadId_ReceivedAtUtc",
                table: "IncomingEmails",
                columns: new[] { "TenantId", "EmailThreadId", "ReceivedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingEmails_TenantId_EmailThreadId_ReceivedAtUtc",
                table: "IncomingEmails"
            );
        }
    }
}
