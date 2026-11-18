using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPrimaryEmailOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El único de email está escrito a mano (EF no lo conoce): se quita antes de cambiar la columna.
            migrationBuilder.Sql("DROP INDEX UX_Customers_Tenant_PrimaryEmailNormalized ON Customers;");

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryEmailNormalized",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254
            );

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryEmail",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254
            );

            // D-3: varios clientes sin email por oficina; el email, si existe, sigue siendo único.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX UX_Customers_Tenant_PrimaryEmailNormalized
                ON Customers (TenantId, PrimaryEmailNormalized)
                WHERE Status <> 'Archived' AND PrimaryEmailNormalized IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX UX_Customers_Tenant_PrimaryEmailNormalized ON Customers;");

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryEmailNormalized",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryEmail",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254,
                oldNullable: true
            );

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX UX_Customers_Tenant_PrimaryEmailNormalized
                ON Customers (TenantId, PrimaryEmailNormalized)
                WHERE Status <> 'Archived';
                """
            );
        }
    }
}
