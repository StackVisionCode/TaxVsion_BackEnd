using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProjectionVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastProfileVersion",
                table: "Tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastStatusVersion",
                table: "Tenants",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastProfileVersion",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastStatusVersion",
                table: "Tenants");
        }
    }
}
