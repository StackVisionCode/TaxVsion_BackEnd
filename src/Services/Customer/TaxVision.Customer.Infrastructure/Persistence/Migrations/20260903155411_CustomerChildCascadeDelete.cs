using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerChildCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                table: "CustomerAddresses"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerContactPoints_Customers_CustomerId",
                table: "CustomerContactPoints"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRelationFiscalProfiles_CustomerRelations_CustomerRelationId",
                table: "CustomerRelationFiscalProfiles"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRelations_Customers_CustomerId",
                table: "CustomerRelations"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                table: "CustomerAddresses",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerContactPoints_Customers_CustomerId",
                table: "CustomerContactPoints",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRelationFiscalProfiles_CustomerRelations_CustomerRelationId",
                table: "CustomerRelationFiscalProfiles",
                column: "CustomerRelationId",
                principalTable: "CustomerRelations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRelations_Customers_CustomerId",
                table: "CustomerRelations",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                table: "CustomerAddresses"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerContactPoints_Customers_CustomerId",
                table: "CustomerContactPoints"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRelationFiscalProfiles_CustomerRelations_CustomerRelationId",
                table: "CustomerRelationFiscalProfiles"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRelations_Customers_CustomerId",
                table: "CustomerRelations"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                table: "CustomerAddresses",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerContactPoints_Customers_CustomerId",
                table: "CustomerContactPoints",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRelationFiscalProfiles_CustomerRelations_CustomerRelationId",
                table: "CustomerRelationFiscalProfiles",
                column: "CustomerRelationId",
                principalTable: "CustomerRelations",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRelations_Customers_CustomerId",
                table: "CustomerRelations",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id"
            );
        }
    }
}
