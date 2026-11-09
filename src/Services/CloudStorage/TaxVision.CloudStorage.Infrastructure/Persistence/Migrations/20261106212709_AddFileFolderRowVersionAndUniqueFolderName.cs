using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileFolderRowVersionAndUniqueFolderName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Antes de tocar Files: con duplicados se corta aquí, con un mensaje que dice qué hacer.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM [Folders]
                    WHERE [SoftDeletedAtUtc] IS NULL AND [Category] IS NULL
                    GROUP BY [TenantId], [OwnerType], [OwnerId], [ParentFolderId], [Name]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'CST-6: Folders has duplicate names under the same parent and owner. Run Q-CST-6c and rename the newest ones before applying this migration.', 1;
                """
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Folders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Files",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "UX_Folders_Owner_Parent_Name",
                table: "Folders",
                columns: new[] { "TenantId", "OwnerType", "OwnerId", "ParentFolderId", "Name" },
                unique: true,
                filter: "[SoftDeletedAtUtc] IS NULL AND [Category] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Folders_Owner_Parent_Name",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Files");
        }
    }
}
