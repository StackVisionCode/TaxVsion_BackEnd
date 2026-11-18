using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerFiscalProfileTaxIdentifierOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles"
            );

            migrationBuilder.AlterColumn<string>(
                name: "TaxIdentifierLast4",
                table: "CustomerFiscalProfiles",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4)",
                oldMaxLength: 4
            );

            migrationBuilder.AlterColumn<byte[]>(
                name: "TaxIdentifierCipher",
                table: "CustomerFiscalProfiles",
                type: "varbinary(512)",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(512)"
            );

            migrationBuilder.AlterColumn<string>(
                name: "TaxIdentifierBlindIndex",
                table: "CustomerFiscalProfiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64
            );

            migrationBuilder.CreateIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" },
                unique: true,
                filter: "[TaxIdentifierBlindIndex] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles"
            );

            migrationBuilder.AlterColumn<string>(
                name: "TaxIdentifierLast4",
                table: "CustomerFiscalProfiles",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(4)",
                oldMaxLength: 4,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<byte[]>(
                name: "TaxIdentifierCipher",
                table: "CustomerFiscalProfiles",
                type: "varbinary(512)",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "varbinary(512)",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "TaxIdentifierBlindIndex",
                table: "CustomerFiscalProfiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_CustomerFiscalProfiles_Tenant_BlindIndex",
                table: "CustomerFiscalProfiles",
                columns: new[] { "TenantId", "TaxIdentifierBlindIndex" },
                unique: true
            );
        }
    }
}
