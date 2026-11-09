using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftInternetMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "Drafts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_TenantId_InternetMessageId",
                table: "Drafts",
                columns: new[] { "TenantId", "InternetMessageId" },
                filter: "[InternetMessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drafts_TenantId_InternetMessageId",
                table: "Drafts");

            migrationBuilder.DropColumn(
                name: "InternetMessageId",
                table: "Drafts");
        }
    }
}
