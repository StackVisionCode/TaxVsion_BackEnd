using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionStartIpAndRevokedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByUserId",
                table: "UserSessions",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "StartIpAddress",
                table: "UserSessions",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RevokedByUserId", table: "UserSessions");

            migrationBuilder.DropColumn(name: "StartIpAddress", table: "UserSessions");
        }
    }
}
