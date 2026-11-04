using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateIndex(
                    name: "IX_IncomingEmails_TenantId_From",
                    table: "IncomingEmails",
                    columns: new[] { "TenantId", "From" }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[]
                    {
                        "FromDisplayName",
                        "ReceivedAtUtc",
                        "EmailThreadId",
                        "DeletedAtUtc",
                        "CustomerId",
                        "AccountId",
                    }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_DraftRecipients_TenantId_Address",
                    table: "DraftRecipients",
                    columns: new[] { "TenantId", "Address" }
                )
                .Annotation("SqlServer:Include", new[] { "DisplayName", "DraftId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_IncomingEmails_TenantId_From", table: "IncomingEmails");

            migrationBuilder.DropIndex(name: "IX_DraftRecipients_TenantId_Address", table: "DraftRecipients");
        }
    }
}
