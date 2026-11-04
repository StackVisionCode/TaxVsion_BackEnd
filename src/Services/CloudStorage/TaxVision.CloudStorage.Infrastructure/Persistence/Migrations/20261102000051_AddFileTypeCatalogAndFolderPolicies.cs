using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTypeCatalogAndFolderPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanFileTypeGroups",
                table: "TenantStorageLimits",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "documents,spreadsheets,images,data,media"
            );

            migrationBuilder.AddColumn<string>(
                name: "PortalFileTypeGroups",
                table: "TenantStorageLimits",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "PortalMaxFileBytes",
                table: "TenantStorageLimits",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "FilePolicyAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    After = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilePolicyAudits", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FileTypeDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContentTypes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTypeDefinitions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FolderFilePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MaxSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AllowedExtensions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UsesPlanFileTypes = table.Column<bool>(type: "bit", nullable: false),
                    UsesPlanFileSize = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderFilePolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(name: "IX_FilePolicyAudits_AtUtc", table: "FilePolicyAudits", column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FileTypeDefinitions_Extension",
                table: "FileTypeDefinitions",
                column: "Extension",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FolderFilePolicies_FolderType",
                table: "FolderFilePolicies",
                column: "FolderType",
                unique: true
            );

            // Semilla única con los valores que vivían en CloudStorageOptions/appsettings (nada cambia al desplegar).
            // Desde acá los administra PlatformAdmin (storage/admin/file-policies); no es HasData a propósito.
            migrationBuilder.InsertData(
                table: "FileTypeDefinitions",
                columns: new[]
                {
                    "Id",
                    "Extension",
                    "Group",
                    "ContentTypes",
                    "IsEnabled",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                },
                values: new object[,]
                {
                    {
                        new Guid("a743cbe3-46ae-5073-85bb-0e35ac50df50"),
                        ".pdf",
                        "documents",
                        "application/pdf",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("f12f17f1-7da9-5921-ae97-126eb02f620d"),
                        ".doc",
                        "documents",
                        "application/msword",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("7efb61b8-1b57-51a1-acf3-4f61b2f5670e"),
                        ".docx",
                        "documents",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("7ef82358-8a34-578f-a51c-f4f305e57dcc"),
                        ".txt",
                        "documents",
                        "text/plain",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("409325f3-2439-56c4-bdb9-f1f4e98ebbcd"),
                        ".xls",
                        "spreadsheets",
                        "application/vnd.ms-excel",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("1bd10fb2-1d0c-51f5-9533-03021c982929"),
                        ".xlsx",
                        "spreadsheets",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("0e08b986-5be9-5c4f-acfc-e0ee97d733cb"),
                        ".csv",
                        "spreadsheets",
                        "text/csv",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("704f5805-bbea-501e-adf3-a243d4dede6a"),
                        ".jpg",
                        "images",
                        "image/jpeg",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("e82d98f3-7bbf-5261-9b08-eebd776c5178"),
                        ".jpeg",
                        "images",
                        "image/jpeg",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("f0bc8427-5cba-53ee-b5d4-8f1ba5693cd5"),
                        ".png",
                        "images",
                        "image/png",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("22a6aa06-3556-54f4-acf2-acc04b741a69"),
                        ".tif",
                        "images",
                        "image/tiff",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("716c5529-a8e6-5f1c-8ac0-d0cb7c7e9369"),
                        ".tiff",
                        "images",
                        "image/tiff",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("e853bfa2-d69c-5dd2-b12f-b40fa58fcd51"),
                        ".json",
                        "data",
                        "application/json",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("89622658-6eb3-5841-a83a-1da40c1111e4"),
                        ".html",
                        "data",
                        "text/html",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("afa27510-13d3-50ae-900a-b7b2559748c0"),
                        ".webm",
                        "media",
                        "video/webm,audio/webm",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("55e5dbff-fbd6-5779-800a-ece5adacb488"),
                        ".mp4",
                        "media",
                        "video/mp4,audio/mp4",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("5acfcd4f-6fb8-5a6d-a04f-fa2e1d251e26"),
                        ".ppt",
                        "presentations",
                        "application/vnd.ms-powerpoint",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("69a2423a-5f7e-5545-959c-d99f76e76d94"),
                        ".pptx",
                        "presentations",
                        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("5c284d54-6dbc-593d-84bc-ac051c7189c0"),
                        ".rtf",
                        "rich_text",
                        "application/rtf",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("d55836a6-503e-5f03-96cf-a1d766e94473"),
                        ".gif",
                        "web_images",
                        "image/gif",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("dbcb4a58-dc68-53a2-8cbb-d6137a31c816"),
                        ".webp",
                        "web_images",
                        "image/webp",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("f32b08e2-5d21-5660-aeaf-11b1eafd20e9"),
                        ".zip",
                        "archives",
                        "application/zip",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("2318b95f-1de0-5db9-9abb-1a2fc1f93238"),
                        ".xml",
                        "xml",
                        "application/xml,text/xml",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("4dffc69a-4479-57a1-8061-49347e3f8ac7"),
                        ".svg",
                        "branding",
                        "image/svg+xml",
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                }
            );

            migrationBuilder.InsertData(
                table: "FolderFilePolicies",
                columns: new[]
                {
                    "Id",
                    "FolderType",
                    "MaxSizeBytes",
                    "AllowedExtensions",
                    "UsesPlanFileTypes",
                    "UsesPlanFileSize",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                },
                values: new object[,]
                {
                    {
                        new Guid("7112a6fa-0ad5-51a6-b7e0-c150d432e54d"),
                        "Documents",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("9366d1fa-cfcd-5931-9b0a-4e65d60b0468"),
                        "Receipts",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("da18f829-dcc1-558a-9125-b87e6df9fa40"),
                        "Invoices",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("f824b5ab-cdb2-55ed-ae24-bfc50e7d76c4"),
                        "EmailIncoming",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("d776bf4e-883a-553b-a218-009934959b1f"),
                        "EmailOutgoing",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("d1aebef3-036c-5a5d-8085-7a7e31292fa6"),
                        "Signatures",
                        26214400L,
                        ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.rtf,.xml,.json,.jpg,.jpeg,.png,.tif,.tiff,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("1eeda99b-9a24-58eb-a696-47e865b78eb3"),
                        "Tasks",
                        26214400L,
                        ".pdf,.docx,.xlsx,.jpg,.jpeg,.png,.txt,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("ca86b9f1-2182-5cc6-adfd-f6352924644a"),
                        "Avatars",
                        5242880L,
                        ".jpg,.jpeg,.png,.webp",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("1ed0b5cd-523a-58dd-b837-1d094900b3bf"),
                        "Imports",
                        104857600L,
                        ".csv,.xlsx,.zip",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("cb701f2b-a2a8-5292-b6da-23e84ad7fedb"),
                        "Recordings",
                        524288000L,
                        ".webm,.mp4",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("0a1ce35f-e92a-5db1-b3a6-ae46482572d9"),
                        "RecordingTracks",
                        524288000L,
                        ".webm",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("40be9d4d-09a3-577b-b700-2a49c6ab5b11"),
                        "Transcripts",
                        5242880L,
                        ".txt,.json",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("3f97bd27-bf00-5410-8b40-edff925ec737"),
                        "Templates",
                        5242880L,
                        ".html,.txt,.json,.png",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("6b1446e0-7e23-585a-9dd9-9d121349032d"),
                        "Branding",
                        512000L,
                        ".png,.jpg,.jpeg,.svg",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("3e9ebb80-7c47-5434-8154-e6f78dfa708f"),
                        "SaaSReceipts",
                        5242880L,
                        ".pdf",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("59dcf3ca-4212-5ed1-b4d6-0dae08fb553c"),
                        "ClientForms",
                        26214400L,
                        ".pdf,.jpg,.jpeg,.png,.docx,.xlsx,.csv,.txt",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("479f069a-2d4c-51aa-9e6d-b9dbd360e685"),
                        "Backups",
                        209715200L,
                        ".zip,.json,.csv",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("a23d5ddd-b330-5f6b-8688-0b40b26afc56"),
                        "VoiceNotes",
                        52428800L,
                        ".webm,.mp4",
                        false,
                        false,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    {
                        new Guid("6b1c182b-f61c-5b0b-8e5a-170203517497"),
                        "Other",
                        26214400L,
                        ".pdf,.docx,.xlsx,.jpg,.jpeg,.png,.txt,.csv,.zip,.json",
                        true,
                        true,
                        Guid.Empty,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                }
            );

            // Hasta el próximo entitlements_changed, Pro/Enterprise conservan los grupos que les daba appsettings.
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE [TenantStorageLimits] SET [PlanFileTypeGroups] = "
                    + "N''documents,spreadsheets,images,data,media,presentations,rich_text,web_images,archives,xml'' "
                    + "WHERE [PlanCode] IN (N''pro'', N''enterprise'')';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FilePolicyAudits");

            migrationBuilder.DropTable(name: "FileTypeDefinitions");

            migrationBuilder.DropTable(name: "FolderFilePolicies");

            migrationBuilder.DropColumn(name: "PlanFileTypeGroups", table: "TenantStorageLimits");

            migrationBuilder.DropColumn(name: "PortalFileTypeGroups", table: "TenantStorageLimits");

            migrationBuilder.DropColumn(name: "PortalMaxFileBytes", table: "TenantStorageLimits");
        }
    }
}
