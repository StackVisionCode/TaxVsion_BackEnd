using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationChallengeSuperseded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "EmailVerificationChallenges",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationChallenges_Email_SupersededAtUtc_VerifiedAtUtc_ExpiresAtUtc",
                table: "EmailVerificationChallenges",
                columns: new[] { "Email", "SupersededAtUtc", "VerifiedAtUtc", "ExpiresAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailVerificationChallenges_Email_SupersededAtUtc_VerifiedAtUtc_ExpiresAtUtc",
                table: "EmailVerificationChallenges"
            );

            migrationBuilder.DropColumn(name: "SupersededAtUtc", table: "EmailVerificationChallenges");
        }
    }
}
