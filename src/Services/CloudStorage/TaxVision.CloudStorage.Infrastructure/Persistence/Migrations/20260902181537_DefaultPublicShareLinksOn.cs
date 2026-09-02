using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefaultPublicShareLinksOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "AllowPublicShareLinks",
                table: "TenantStorageLimits",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            // Los tenants existentes tenian el flag en 0 solo porque ese era el default anterior:
            // nunca hubo UI para desactivarlo, asi que cada 0 es un default, no una decision. Se
            // pasan todos a 1 para alinearlos con el nuevo default (activado). Las desactivaciones
            // futuras las hara el Tenant Admin desde Settings.
            migrationBuilder.Sql("UPDATE [TenantStorageLimits] SET [AllowPublicShareLinks] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "AllowPublicShareLinks",
                table: "TenantStorageLimits",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);
        }
    }
}
