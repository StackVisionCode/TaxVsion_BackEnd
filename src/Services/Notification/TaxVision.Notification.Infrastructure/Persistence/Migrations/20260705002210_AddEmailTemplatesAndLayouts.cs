using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplatesAndLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LayoutName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    HtmlStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    HtmlFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DesignStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DesignFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviewStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PreviewFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLayouts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VariablesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HtmlStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    HtmlFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DesignFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviewStorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PreviewFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateVersions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailLayouts_Scope_TenantId",
                table: "EmailLayouts",
                columns: new[] { "Scope", "TenantId" },
                unique: true,
                filter: "[IsDefault] = 1"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailLayouts_TenantId_Scope",
                table: "EmailLayouts",
                columns: new[] { "TenantId", "Scope" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Scope_TenantId_TemplateKey",
                table: "EmailTemplates",
                columns: new[] { "Scope", "TenantId", "TemplateKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_TenantId_Scope",
                table: "EmailTemplates",
                columns: new[] { "TenantId", "Scope" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplateVersions_TemplateId_VersionNumber",
                table: "EmailTemplateVersions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailLayouts");

            migrationBuilder.DropTable(name: "EmailTemplates");

            migrationBuilder.DropTable(name: "EmailTemplateVersions");
        }
    }
}
