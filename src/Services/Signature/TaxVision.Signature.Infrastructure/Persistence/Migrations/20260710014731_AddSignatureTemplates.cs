using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignatureTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DefaultTokenExpirationHours = table.Column<int>(type: "int", nullable: false),
                    RequiresSequentialSigning = table.Column<bool>(type: "bit", nullable: false),
                    RequiresConsent = table.Column<bool>(type: "bit", nullable: false),
                    GenerateCertificate = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureTemplates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TemplateFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position_Page = table.Column<int>(type: "int", nullable: false),
                    Position_X = table.Column<double>(type: "float", nullable: false),
                    Position_Y = table.Column<double>(type: "float", nullable: false),
                    Position_Width = table.Column<double>(type: "float", nullable: false),
                    Position_Height = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateFields_SignatureTemplates_SignatureTemplateId",
                        column: x => x.SignatureTemplateId,
                        principalTable: "SignatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "TemplateSignerSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DefaultLanguage = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSignerSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateSignerSlots_SignatureTemplates_SignatureTemplateId",
                        column: x => x.SignatureTemplateId,
                        principalTable: "SignatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureTemplates_TenantId_Category",
                table: "SignatureTemplates",
                columns: new[] { "TenantId", "Category" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SignatureTemplates_TenantId_Status",
                table: "SignatureTemplates",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_SignatureTemplateId_SlotOrder",
                table: "TemplateFields",
                columns: new[] { "SignatureTemplateId", "SlotOrder" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSignerSlots_SignatureTemplateId_Order",
                table: "TemplateSignerSlots",
                columns: new[] { "SignatureTemplateId", "Order" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TemplateFields");

            migrationBuilder.DropTable(name: "TemplateSignerSlots");

            migrationBuilder.DropTable(name: "SignatureTemplates");
        }
    }
}
