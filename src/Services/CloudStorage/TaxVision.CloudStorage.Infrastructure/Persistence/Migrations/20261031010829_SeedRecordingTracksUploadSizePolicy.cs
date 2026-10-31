using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedRecordingTracksUploadSizePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F7: tope Global de RecordingTracks en 2 GB, igual que Recordings (una pista de audio de una
            // reunión larga no debe caer al tope genérico del plan). InsertData y no HasData, como D-37.
            migrationBuilder.InsertData(
                table: "UploadSizePolicies",
                columns: new[]
                {
                    "Id",
                    "Scope",
                    "FolderType",
                    "PlanCode",
                    "TenantId",
                    "MaxFileSizeBytes",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                },
                values: new object[]
                {
                    new Guid("c1000000-0000-0000-0000-000000000002"),
                    "Global",
                    "RecordingTracks",
                    null,
                    null,
                    2147483648L,
                    Guid.Empty,
                    new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "UploadSizePolicies",
                keyColumn: "Id",
                keyValue: new Guid("c1000000-0000-0000-0000-000000000002")
            );
        }
    }
}
