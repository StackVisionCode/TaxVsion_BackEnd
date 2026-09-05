using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenRolePermissionsProjectionCodesToMax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PermissionCodesJson",
                table: "AuthzRolePermissionsProjections",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PermissionCodesJson",
                table: "AuthzRolePermissionsProjections",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)"
            );
        }
    }
}
