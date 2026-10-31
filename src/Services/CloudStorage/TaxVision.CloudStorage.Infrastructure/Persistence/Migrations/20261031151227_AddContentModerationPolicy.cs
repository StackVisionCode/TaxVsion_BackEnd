using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentModerationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentModerationPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    BlockThreshold = table.Column<double>(type: "float", nullable: false),
                    ReviewThreshold = table.Column<double>(type: "float", nullable: false),
                    ScanVideos = table.Column<bool>(type: "bit", nullable: false),
                    FrameIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxFrames = table.Column<int>(type: "int", nullable: false),
                    MaxScanBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExcludedFolders = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FailClosed = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentModerationPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentModerationPolicyAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentModerationPolicyAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentModerationPolicyAudits_OccurredAt",
                table: "ContentModerationPolicyAudits",
                column: "OccurredAtUtc");

            // T-20: fila única de la política, estricta por defecto (no hay uso legítimo de imágenes sexuales
            // en una oficina de impuestos). Se omiten carpetas que solo escriben servicios con contenido propio.
            // InsertData y no HasData, como D-37.
            migrationBuilder.InsertData(
                table: "ContentModerationPolicies",
                columns: new[]
                {
                    "Id",
                    "Enabled",
                    "BlockThreshold",
                    "ReviewThreshold",
                    "ScanVideos",
                    "FrameIntervalSeconds",
                    "MaxFrames",
                    "MaxScanBytes",
                    "ExcludedFolders",
                    "FailClosed",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                },
                values: new object[]
                {
                    new Guid("5c0d7e1a-7f3b-4c2e-9a51-0b6f1d2e3c40"),
                    true,
                    0.90,
                    0.60,
                    true,
                    2,
                    30,
                    268435456L,
                    "Backups,Branding,RecordingTracks,Recordings,Templates,Transcripts",
                    true,
                    Guid.Empty,
                    new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentModerationPolicies");

            migrationBuilder.DropTable(
                name: "ContentModerationPolicyAudits");
        }
    }
}
