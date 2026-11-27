using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserCredentialState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CredentialStateChangedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CredentialStateChangedByUserId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<byte>(
                name: "CredentialStateReasonCode",
                table: "Users",
                type: "tinyint",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "CredentialStateReasonNote",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true
            );

            migrationBuilder.AddColumn<byte>(
                name: "CredentialStatus",
                table: "Users",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CredentialStateChangedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "CredentialStateChangedByUserId", table: "Users");

            migrationBuilder.DropColumn(name: "CredentialStateReasonCode", table: "Users");

            migrationBuilder.DropColumn(name: "CredentialStateReasonNote", table: "Users");

            migrationBuilder.DropColumn(name: "CredentialStatus", table: "Users");
        }
    }
}
