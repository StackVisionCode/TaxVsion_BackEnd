using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureRequestsSummaryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder
                .CreateIndex(
                    name: "IX_Signers_MappedCustomerId_SignatureRequestId",
                    table: "Signers",
                    column: "MappedCustomerId",
                    filter: "[MappedCustomerId] IS NOT NULL"
                )
                .Annotation("SqlServer:Include", new[] { "SignatureRequestId" });

            migrationBuilder
                .CreateIndex(
                    name: "IX_SignatureRequests_TenantId_Status_CompletedAtUtc",
                    table: "SignatureRequests",
                    columns: new[] { "TenantId", "Status", "CompletedAtUtc" }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[] { "SentAtUtc", "ExpiresAtUtc", "ExpiredAtUtc", "CanceledAtUtc", "CreatedByUserId" }
                );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Signers_MappedCustomerId_SignatureRequestId", table: "Signers");

            migrationBuilder.DropIndex(
                name: "IX_SignatureRequests_TenantId_Status_CompletedAtUtc",
                table: "SignatureRequests"
            );
        }
    }
}
