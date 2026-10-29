using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPendingProfilePicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PendingProfilePictureFileId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingProfilePictureRequestedAtUtc",
                table: "Customers",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PendingProfilePictureRequestedAtUtc",
                table: "Customers",
                column: "PendingProfilePictureRequestedAtUtc",
                filter: "[PendingProfilePictureRequestedAtUtc] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Customers_PendingProfilePictureRequestedAtUtc", table: "Customers");

            migrationBuilder.DropColumn(name: "PendingProfilePictureFileId", table: "Customers");

            migrationBuilder.DropColumn(name: "PendingProfilePictureRequestedAtUtc", table: "Customers");
        }
    }
}
