using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Documents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentBrandings",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    LogoDataUri = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BrandColorHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentBrandings", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_DocumentBrandings_Tenant",
                schema: "documents",
                table: "DocumentBrandings",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentBrandings", schema: "documents");
        }
    }
}
