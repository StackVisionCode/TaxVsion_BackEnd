using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftReconciliationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_Draft_CreatedAtUtc",
                table: "SignatureRequests",
                column: "CreatedAtUtc",
                filter: "[Status] = 'Draft'"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SignatureRequests_Draft_CreatedAtUtc", table: "SignatureRequests");
        }
    }
}
