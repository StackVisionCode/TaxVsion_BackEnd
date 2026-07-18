using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSignatureSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowedVerificationChannels = table.Column<int>(type: "int", nullable: false),
                    DefaultVerificationChannel = table.Column<int>(type: "int", nullable: false),
                    DefaultTokenExpirationHoursValue = table.Column<int>(type: "int", nullable: false),
                    RemindersEnabledByDefault = table.Column<bool>(type: "bit", nullable: false),
                    GenerateCertificateByDefault = table.Column<bool>(type: "bit", nullable: false),
                    Limits_MaxPdfBytes = table.Column<long>(type: "bigint", nullable: false),
                    Limits_MaxImageBytes = table.Column<long>(type: "bigint", nullable: false),
                    Limits_MaxPagesPerDocument = table.Column<int>(type: "int", nullable: false),
                    Retention_Years = table.Column<int>(type: "int", nullable: false),
                    Retention_AllowPurge = table.Column<bool>(type: "bit", nullable: false),
                    AuditSecretEncrypted = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AuditKeyVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSignatureSettings", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSignatureSettings_TenantId",
                table: "TenantSignatureSettings",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantSignatureSettings");
        }
    }
}
