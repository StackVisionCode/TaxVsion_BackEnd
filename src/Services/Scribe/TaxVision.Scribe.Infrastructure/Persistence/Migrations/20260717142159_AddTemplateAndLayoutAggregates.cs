using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAndLayoutAggregates : Migration
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
                    LayoutKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    TemplateKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EventTemplateMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTemplateMappings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailLayoutVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailLayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HtmlStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HtmlFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignJsonStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DesignJsonFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviewImageStorageKey = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    PreviewImageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLayoutVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailLayoutVersions_EmailLayouts_EmailLayoutId",
                        column: x => x.EmailLayoutId,
                        principalTable: "EmailLayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HtmlStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HtmlFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TextFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DesignJsonStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DesignJsonFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviewImageStorageKey = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    PreviewImageFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LayoutVersionNumber = table.Column<int>(type: "int", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTemplateVersions_EmailTemplates_EmailTemplateId",
                        column: x => x.EmailTemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "TemplateVariableDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVariableDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateVariableDefinitions_EmailTemplateVersions_EmailTemplateVersionId",
                        column: x => x.EmailTemplateVersionId,
                        principalTable: "EmailTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailLayouts_Scope_TenantId_LayoutKey",
                table: "EmailLayouts",
                columns: new[] { "Scope", "TenantId", "LayoutKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailLayoutVersions_EmailLayoutId_Status",
                table: "EmailLayoutVersions",
                columns: new[] { "EmailLayoutId", "Status" },
                unique: true,
                filter: "[Status] = 'Published'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailLayoutVersions_EmailLayoutId_VersionNumber",
                table: "EmailLayoutVersions",
                columns: new[] { "EmailLayoutId", "VersionNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_Scope_TenantId_TemplateKey",
                table: "EmailTemplates",
                columns: new[] { "Scope", "TenantId", "TemplateKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplateVersions_EmailTemplateId_Status",
                table: "EmailTemplateVersions",
                columns: new[] { "EmailTemplateId", "Status" },
                unique: true,
                filter: "[Status] = 'Published'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplateVersions_EmailTemplateId_VersionNumber",
                table: "EmailTemplateVersions",
                columns: new[] { "EmailTemplateId", "VersionNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventTemplateMappings_EventKey_Enabled",
                table: "EventTemplateMappings",
                columns: new[] { "EventKey", "Enabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventTemplateMappings_Scope_TenantId_EventKey_Locale",
                table: "EventTemplateMappings",
                columns: new[] { "Scope", "TenantId", "EventKey", "Locale" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVariableDefinitions_EmailTemplateVersionId",
                table: "TemplateVariableDefinitions",
                column: "EmailTemplateVersionId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailLayoutVersions");

            migrationBuilder.DropTable(name: "EventTemplateMappings");

            migrationBuilder.DropTable(name: "TemplateVariableDefinitions");

            migrationBuilder.DropTable(name: "EmailLayouts");

            migrationBuilder.DropTable(name: "EmailTemplateVersions");

            migrationBuilder.DropTable(name: "EmailTemplates");
        }
    }
}
