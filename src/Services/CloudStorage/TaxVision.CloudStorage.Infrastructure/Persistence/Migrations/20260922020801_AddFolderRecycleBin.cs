using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderRecycleBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBatchId",
                table: "Folders",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "SoftDeleteExpiresAtUtc",
                table: "Folders",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "SoftDeletedAtUtc",
                table: "Folders",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBatchId",
                table: "Files",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Folders_SoftDeleteExpiresAtUtc",
                table: "Folders",
                column: "SoftDeleteExpiresAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Folders_TenantId_DeletedBatchId",
                table: "Folders",
                columns: new[] { "TenantId", "DeletedBatchId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Folders_SoftDeleteExpiresAtUtc", table: "Folders");

            migrationBuilder.DropIndex(name: "IX_Folders_TenantId_DeletedBatchId", table: "Folders");

            migrationBuilder.DropColumn(name: "DeletedBatchId", table: "Folders");

            migrationBuilder.DropColumn(name: "SoftDeleteExpiresAtUtc", table: "Folders");

            migrationBuilder.DropColumn(name: "SoftDeletedAtUtc", table: "Folders");

            migrationBuilder.DropColumn(name: "DeletedBatchId", table: "Files");
        }
    }
}
