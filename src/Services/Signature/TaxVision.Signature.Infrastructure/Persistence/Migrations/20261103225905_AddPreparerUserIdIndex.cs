using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparerUserIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_Preparer_UserId",
                table: "SignatureRequests",
                column: "Preparer_UserId",
                filter: "[Preparer_UserId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SignatureRequests_Preparer_UserId", table: "SignatureRequests");
        }
    }
}
