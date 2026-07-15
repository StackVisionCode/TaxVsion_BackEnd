using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDmcaNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudStorageDmcaNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimantName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ClaimantEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CopyrightedWorkDescription = table.Column<string>(
                        type: "nvarchar(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
                    InfringingMaterialDescription = table.Column<string>(
                        type: "nvarchar(2048)",
                        maxLength: 2048,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RegisteredByActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CounterNoticeText = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    CounterNoticeSubmittedByActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CounterNoticeSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudStorageDmcaNotices", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CloudStorageDmcaNotices_TenantId_FileId_Status",
                table: "CloudStorageDmcaNotices",
                columns: new[] { "TenantId", "FileId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CloudStorageDmcaNotices_TenantId_Id",
                table: "CloudStorageDmcaNotices",
                columns: new[] { "TenantId", "Id" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CloudStorageDmcaNotices");
        }
    }
}
