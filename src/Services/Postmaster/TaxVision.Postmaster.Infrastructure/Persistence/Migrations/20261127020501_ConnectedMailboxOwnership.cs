using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectedMailboxOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "ConnectedMailboxes",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "OwnershipKnown",
                table: "ConnectedMailboxes",
                type: "bit",
                nullable: false,
                defaultValue: false
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OwnerUserId", table: "ConnectedMailboxes");

            migrationBuilder.DropColumn(name: "OwnershipKnown", table: "ConnectedMailboxes");
        }
    }
}
