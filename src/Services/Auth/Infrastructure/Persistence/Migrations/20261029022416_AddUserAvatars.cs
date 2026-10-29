using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AvatarFileId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PendingAvatarFileId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingAvatarRequestedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Users_PendingAvatarRequestedAtUtc",
                table: "Users",
                column: "PendingAvatarRequestedAtUtc",
                filter: "[PendingAvatarRequestedAtUtc] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Users_PendingAvatarRequestedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "AvatarFileId", table: "Users");

            migrationBuilder.DropColumn(name: "PendingAvatarFileId", table: "Users");

            migrationBuilder.DropColumn(name: "PendingAvatarRequestedAtUtc", table: "Users");
        }
    }
}
