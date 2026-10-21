using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormsTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DraftDefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DraftHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DraftUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DraftUpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LatestVersionNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LatestVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LatestPublishedHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LibraryOriginKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LibrarySourceTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LibrarySourceVersionNumber = table.Column<int>(type: "int", nullable: true),
                    LibraryKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsLibraryListed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplates", x => x.Id);
                    table.CheckConstraint("CK_FormTemplates_Draft_IsJson", "ISJSON([DraftDefinitionJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "FormTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Presentation = table.Column<int>(type: "int", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ComponentCount = table.Column<int>(type: "int", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplateVersions", x => x.Id);
                    table.CheckConstraint("CK_FormTemplateVersions_Definition_IsJson", "ISJSON([DefinitionJson]) = 1");
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_FormTemplates_FormTemplateId",
                        column: x => x.FormTemplateId,
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAuditEntries_TenantId_Request_OccurredAt",
                table: "FormAuditEntries",
                columns: new[] { "TenantId", "FormRequestId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAuditEntries_TenantId_Template_OccurredAt",
                table: "FormAuditEntries",
                columns: new[] { "TenantId", "FormTemplateId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_TenantId_Status_Name",
                table: "FormTemplates",
                columns: new[] { "TenantId", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplates_LibraryKey",
                table: "FormTemplates",
                column: "LibraryKey",
                unique: true,
                filter: "[LibraryKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_TenantId_TemplateId",
                table: "FormTemplateVersions",
                columns: new[] { "TenantId", "FormTemplateId" });

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplateVersions_TemplateId_VersionNumber",
                table: "FormTemplateVersions",
                columns: new[] { "FormTemplateId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAuditEntries");

            migrationBuilder.DropTable(
                name: "FormTemplateVersions");

            migrationBuilder.DropTable(
                name: "FormTemplates");
        }
    }
}
