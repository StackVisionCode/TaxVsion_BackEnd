using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLogoSurfaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_TenantLogoRefs", table: "TenantLogoRefs");

            // Las filas existentes vinieron del único logo que se publicaba: el del CRM.
            migrationBuilder.AddColumn<string>(
                name: "Surface",
                table: "TenantLogoRefs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Crm"
            );

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenantLogoRefs",
                table: "TenantLogoRefs",
                columns: new[] { "TenantId", "Surface" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_TenantLogoRefs", table: "TenantLogoRefs");

            // Volver a una fila por tenant: se queda la del CRM.
            migrationBuilder.Sql("DELETE FROM [TenantLogoRefs] WHERE [Surface] <> N'Crm';");

            migrationBuilder.DropColumn(name: "Surface", table: "TenantLogoRefs");

            migrationBuilder.AddPrimaryKey(name: "PK_TenantLogoRefs", table: "TenantLogoRefs", column: "TenantId");
        }
    }
}
