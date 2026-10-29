using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalReference_SourceId",
                table: "SignatureRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference_SourceService",
                table: "SignatureRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference_SourceType",
                table: "SignatureRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_SignatureRequests_ExternalReference",
                table: "SignatureRequests",
                columns: new[] { "ExternalReference_SourceService", "ExternalReference_SourceId" },
                unique: true,
                filter: "[ExternalReference_SourceId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_SignatureRequests_ExternalReference", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "ExternalReference_SourceId", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "ExternalReference_SourceService", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "ExternalReference_SourceType", table: "SignatureRequests");
        }
    }
}
