using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparerAndVerificationFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Signers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparerSignedAtUtc",
                table: "SignatureRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PreparerSignedByUserId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Preparer_DisplayName",
                table: "SignatureRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Preparer_PtinOrEfin",
                table: "SignatureRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Preparer_TitleLabel",
                table: "SignatureRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "SignerVerificationChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AnswerHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignerVerificationChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignerVerificationChallenges_Signers_SignerId",
                        column: x => x.SignerId,
                        principalTable: "Signers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignerVerificationChallenges_SignerId_Method_ExpiresAtUtc",
                table: "SignerVerificationChallenges",
                columns: new[] { "SignerId", "Method", "ExpiresAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SignerVerificationChallenges");

            migrationBuilder.DropColumn(name: "PhoneNumber", table: "Signers");

            migrationBuilder.DropColumn(name: "PreparerSignedAtUtc", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "PreparerSignedByUserId", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "Preparer_DisplayName", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "Preparer_PtinOrEfin", table: "SignatureRequests");

            migrationBuilder.DropColumn(name: "Preparer_TitleLabel", table: "SignatureRequests");
        }
    }
}
