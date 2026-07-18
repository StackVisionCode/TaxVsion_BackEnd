using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAutoSavedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplyContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drafts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "DraftRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftRecipients_Drafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "Drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DraftRecipients_DraftId",
                table: "DraftRecipients",
                column: "DraftId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_Status_UpdatedAtUtc",
                table: "Drafts",
                columns: new[] { "Status", "UpdatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc",
                table: "Drafts",
                columns: new[] { "TenantId", "CustomerId", "Status", "UpdatedAtUtc" },
                descending: new[] { false, false, false, true }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DraftRecipients");

            migrationBuilder.DropTable(name: "Drafts");
        }
    }
}
