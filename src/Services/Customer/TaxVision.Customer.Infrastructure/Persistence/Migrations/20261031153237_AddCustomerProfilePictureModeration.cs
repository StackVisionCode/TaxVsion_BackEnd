using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerProfilePictureModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProfilePictureModerationFileId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureModerationState",
                table: "Customers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProfilePictureModerationFileId", table: "Customers");

            migrationBuilder.DropColumn(name: "ProfilePictureModerationState", table: "Customers");
        }
    }
}
