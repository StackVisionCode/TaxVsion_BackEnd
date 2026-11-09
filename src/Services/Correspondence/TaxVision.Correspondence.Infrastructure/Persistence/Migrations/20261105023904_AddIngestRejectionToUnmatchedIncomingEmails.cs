using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestRejectionToUnmatchedIncomingEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "UnmatchedIncomingEmails",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "UnmatchedIncomingEmails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCode",
                table: "UnmatchedIncomingEmails",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionCode",
                table: "UnmatchedIncomingEmails",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "UnmatchedIncomingEmails");

            migrationBuilder.DropColumn(
                name: "InternetMessageId",
                table: "UnmatchedIncomingEmails");

            migrationBuilder.DropColumn(
                name: "ProviderCode",
                table: "UnmatchedIncomingEmails");

            migrationBuilder.DropColumn(
                name: "RejectionCode",
                table: "UnmatchedIncomingEmails");
        }
    }
}
