using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingEmailReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "IncomingEmails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "IncomingEmails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_TenantId_EmailThreadId_Unread",
                table: "IncomingEmails",
                columns: new[] { "TenantId", "EmailThreadId" },
                filter: "[IsRead] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingEmails_TenantId_EmailThreadId_Unread",
                table: "IncomingEmails");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "IncomingEmails");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "IncomingEmails");
        }
    }
}
