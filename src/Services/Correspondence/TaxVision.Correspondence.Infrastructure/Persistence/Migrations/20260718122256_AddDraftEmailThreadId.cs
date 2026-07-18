using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftEmailThreadId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmailThreadId",
                table: "Drafts",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_TenantId_EmailThreadId_Status",
                table: "Drafts",
                columns: new[] { "TenantId", "EmailThreadId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Drafts_TenantId_EmailThreadId_Status", table: "Drafts");

            migrationBuilder.DropColumn(name: "EmailThreadId", table: "Drafts");
        }
    }
}
