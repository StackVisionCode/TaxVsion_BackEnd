using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationConsentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TextLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    TextSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "DocumentValidationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    HasExistingSignatures = table.Column<bool>(type: "bit", nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RejectionCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentValidationRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SignatureAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousChainHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChainHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAuditEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConsentEvents_TenantId_SignatureRequestId_SignerId_AcceptedAtUtc",
                table: "ConsentEvents",
                columns: new[] { "TenantId", "SignatureRequestId", "SignerId", "AcceptedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DocumentValidationRecords_TenantId_ContentSha256",
                table: "DocumentValidationRecords",
                columns: new[] { "TenantId", "ContentSha256" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DocumentValidationRecords_TenantId_CreatedAtUtc",
                table: "DocumentValidationRecords",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAuditEvents_SignatureRequestId_Sequence",
                table: "SignatureAuditEvents",
                columns: new[] { "SignatureRequestId", "Sequence" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAuditEvents_TenantId_OccurredAtUtc",
                table: "SignatureAuditEvents",
                columns: new[] { "TenantId", "OccurredAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConsentEvents");

            migrationBuilder.DropTable(name: "DocumentValidationRecords");

            migrationBuilder.DropTable(name: "SignatureAuditEvents");
        }
    }
}
