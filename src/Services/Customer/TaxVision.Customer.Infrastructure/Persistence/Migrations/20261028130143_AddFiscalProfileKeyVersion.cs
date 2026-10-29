using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalProfileKeyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "TaxIdentifierKeyVersion",
                table: "CustomerRelationFiscalProfiles",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1
            );

            migrationBuilder.AddColumn<short>(
                name: "TaxIdentifierKeyVersion",
                table: "CustomerFiscalProfiles",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TaxIdentifierKeyVersion", table: "CustomerRelationFiscalProfiles");

            migrationBuilder.DropColumn(name: "TaxIdentifierKeyVersion", table: "CustomerFiscalProfiles");
        }
    }
}
