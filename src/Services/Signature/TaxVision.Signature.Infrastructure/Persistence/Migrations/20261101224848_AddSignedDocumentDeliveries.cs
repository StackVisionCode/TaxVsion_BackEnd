using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedDocumentDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Delivery_AllowPublicReissue",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "Delivery_AttachDocuments",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<int>(
                name: "Delivery_LinkLifetimeDays",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 30
            );

            migrationBuilder.AddColumn<long>(
                name: "Delivery_MaxAttachmentBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 10485760L
            );

            migrationBuilder.CreateTable(
                name: "SignedDocumentDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastIssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssueCount = table.Column<int>(type: "int", nullable: false),
                    AccessCount = table.Column<int>(type: "int", nullable: false),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedDocumentDeliveries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SignedDocumentDeliveryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedDocumentDeliveryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignedDocumentDeliveryItems_SignedDocumentDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "SignedDocumentDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocumentDeliveries_SignatureRequestId_SignerId_Purpose",
                table: "SignedDocumentDeliveries",
                columns: new[] { "SignatureRequestId", "SignerId", "Purpose" },
                unique: true,
                filter: "[Purpose] = 'Completed'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocumentDeliveries_TenantId_SignatureRequestId",
                table: "SignedDocumentDeliveries",
                columns: new[] { "TenantId", "SignatureRequestId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocumentDeliveries_TokenHash",
                table: "SignedDocumentDeliveries",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignedDocumentDeliveryItems_DeliveryId",
                table: "SignedDocumentDeliveryItems",
                column: "DeliveryId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SignedDocumentDeliveryItems");

            migrationBuilder.DropTable(name: "SignedDocumentDeliveries");

            migrationBuilder.DropColumn(name: "Delivery_AllowPublicReissue", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "Delivery_AttachDocuments", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "Delivery_LinkLifetimeDays", table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(name: "Delivery_MaxAttachmentBytes", table: "TenantSignatureSettings");
        }
    }
}
