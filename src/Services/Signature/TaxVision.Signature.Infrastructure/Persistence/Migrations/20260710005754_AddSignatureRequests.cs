using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentHashPre = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SealedFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentHashPost = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CertificateFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequiresSequentialSigning = table.Column<bool>(type: "bit", nullable: false),
                    RequiresConsent = table.Column<bool>(type: "bit", nullable: false),
                    GenerateCertificate = table.Column<bool>(type: "bit", nullable: false),
                    TokenExpirationHours = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevocationEpoch = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedBySignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureRequests", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Signers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MappedCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Signers_SignatureRequests_SignatureRequestId",
                        column: x => x.SignatureRequestId,
                        principalTable: "SignatureRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SignatureFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position_Page = table.Column<int>(type: "int", nullable: false),
                    Position_X = table.Column<double>(type: "float", nullable: false),
                    Position_Y = table.Column<double>(type: "float", nullable: false),
                    Position_Width = table.Column<double>(type: "float", nullable: false),
                    Position_Height = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureFields_Signers_SignerId",
                        column: x => x.SignerId,
                        principalTable: "Signers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFields_SignerId",
                table: "SignatureFields",
                column: "SignerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId_CreatedAtUtc",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId_ExpiresAtUtc",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "ExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId_Status",
                table: "SignatureRequests",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(name: "IX_Signers_Email", table: "Signers", column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Signers_SignatureRequestId_Order",
                table: "Signers",
                columns: new[] { "SignatureRequestId", "Order" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SignatureFields");

            migrationBuilder.DropTable(name: "Signers");

            migrationBuilder.DropTable(name: "SignatureRequests");
        }
    }
}
