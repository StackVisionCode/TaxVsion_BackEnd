using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetTokenLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NotificationEventId",
                table: "PasswordResetTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<byte>(
                name: "Origin",
                table: "PasswordResetTokens",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "PasswordResetTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<byte>(
                name: "RevocationReason",
                table: "PasswordResetTokens",
                type: "tinyint",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByUserId",
                table: "PasswordResetTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PasswordResetTokens",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_PasswordResetTokens_UserId_CreatedAtUtc",
                    table: "PasswordResetTokens",
                    columns: new[] { "UserId", "CreatedAtUtc" },
                    descending: new[] { false, true }
                )
                .Annotation(
                    "SqlServer:Include",
                    new[] { "Origin", "UsedAtUtc", "RevokedAtUtc", "ExpiresAtUtc", "RequestedByUserId" }
                );

            // Hasta hoy el único motivo de anulación era el cambio de contraseña.
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE [PasswordResetTokens] SET [RevocationReason] = 1 WHERE [RevokedAtUtc] IS NOT NULL AND [RevocationReason] IS NULL';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId_CreatedAtUtc",
                table: "PasswordResetTokens"
            );

            migrationBuilder.DropColumn(name: "NotificationEventId", table: "PasswordResetTokens");

            migrationBuilder.DropColumn(name: "Origin", table: "PasswordResetTokens");

            migrationBuilder.DropColumn(name: "RequestedByUserId", table: "PasswordResetTokens");

            migrationBuilder.DropColumn(name: "RevocationReason", table: "PasswordResetTokens");

            migrationBuilder.DropColumn(name: "RevokedByUserId", table: "PasswordResetTokens");

            migrationBuilder.DropColumn(name: "RowVersion", table: "PasswordResetTokens");
        }
    }
}
