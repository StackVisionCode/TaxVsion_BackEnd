using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Folders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Folders_Owner_Category",
                table: "Folders",
                columns: new[] { "TenantId", "OwnerType", "OwnerId", "Category" },
                unique: true,
                filter: "[Category] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Folders_Owner_Category", table: "Folders");

            migrationBuilder.DropColumn(name: "Category", table: "Folders");
        }
    }
}
