using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "IncomingEmails",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Drafts",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "IncomingEmails");

            migrationBuilder.DropColumn(name: "DeletedAtUtc", table: "Drafts");
        }
    }
}
