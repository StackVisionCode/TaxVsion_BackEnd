using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureLimitsAndPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Limits_MaxImageBytes",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxAllowedImageBytes",
                table: "TenantSignatureSettings");

            migrationBuilder.AlterColumn<bool>(
                name: "SendSealedDocumentDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Plan_PurgeAllowed",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MinRetentionYears",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 7);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MaxTokenExpirationHours",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 720);

            migrationBuilder.AlterColumn<long>(
                name: "Plan_MaxAllowedPdfBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 26214400L);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MaxAllowedPages",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 100);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_AllowedChannels",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 3);

            // Plan_* pasa a ser la excepción del tenant (nulo = hereda del plan). Donde la columna tenía el valor
            // que traía todo tenant por defecto no hay excepción real: queda nula y manda el plan (mismo valor hoy).
            migrationBuilder.Sql(
                @"UPDATE TenantSignatureSettings SET
                    Plan_MaxAllowedPdfBytes = CASE WHEN Plan_MaxAllowedPdfBytes = 26214400 THEN NULL ELSE Plan_MaxAllowedPdfBytes END,
                    Plan_MaxAllowedPages = CASE WHEN Plan_MaxAllowedPages = 100 THEN NULL ELSE Plan_MaxAllowedPages END,
                    Plan_MinRetentionYears = CASE WHEN Plan_MinRetentionYears = 7 THEN NULL ELSE Plan_MinRetentionYears END,
                    Plan_PurgeAllowed = CASE WHEN Plan_PurgeAllowed = 0 THEN NULL ELSE Plan_PurgeAllowed END,
                    Plan_AllowedChannels = CASE WHEN Plan_AllowedChannels = 3 THEN NULL ELSE Plan_AllowedChannels END,
                    Plan_MaxTokenExpirationHours = CASE WHEN Plan_MaxTokenExpirationHours = 720 THEN NULL ELSE Plan_MaxTokenExpirationHours END;"
            );

            migrationBuilder.AddColumn<int>(
                name: "Plan_MaxDocumentsPerRequest",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Plan_MaxSignersPerRequest",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignaturePlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OtpLength = table.Column<int>(type: "int", nullable: false),
                    OtpLifetimeMinutes = table.Column<int>(type: "int", nullable: false),
                    OtpResendCooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxVerificationAttempts = table.Column<int>(type: "int", nullable: false),
                    VerificationLockMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxSignatureImageBytes = table.Column<int>(type: "int", nullable: false),
                    MaxProfilesPerPreparer = table.Column<int>(type: "int", nullable: false),
                    MaxRolesPerTemplate = table.Column<int>(type: "int", nullable: false),
                    MaxRemindersPerRequest = table.Column<int>(type: "int", nullable: false),
                    MaxFieldsPerSigner = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignaturePlatformSettings", x => x.Id);
                });

            // Los valores que tenía el código (SignaturePlatformSettings.Defaults()): sin cambio al desplegar.
            migrationBuilder.InsertData(
                table: "SignaturePlatformSettings",
                columns: new[]
                {
                    "Id", "OtpLength", "OtpLifetimeMinutes", "OtpResendCooldownSeconds", "MaxVerificationAttempts",
                    "VerificationLockMinutes", "MaxSignatureImageBytes", "MaxProfilesPerPreparer", "MaxRolesPerTemplate",
                    "MaxRemindersPerRequest", "MaxFieldsPerSigner", "UpdatedByUserId", "UpdatedAtUtc",
                },
                values: new object[]
                {
                    new Guid("7a2c9e4b-1f3d-4b8a-9c6e-2d5f8a1b3c70"), 6, 10, 30, 5,
                    30, 1500000, 10, 20,
                    20, 200, Guid.Empty, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                });

            migrationBuilder.CreateTable(
                name: "TenantSignatureLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                    MaxDocumentsPerRequest = table.Column<int>(type: "int", nullable: false),
                    MaxSignersPerRequest = table.Column<int>(type: "int", nullable: false),
                    MaxPdfBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxPagesPerDocument = table.Column<int>(type: "int", nullable: false),
                    MaxTokenHours = table.Column<int>(type: "int", nullable: false),
                    MinRetentionYears = table.Column<int>(type: "int", nullable: false),
                    PurgeAllowed = table.Column<bool>(type: "bit", nullable: false),
                    AllowedChannels = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSignatureLimits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSignatureLimits_TenantId",
                table: "TenantSignatureLimits",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Vuelven a ser obligatorias: lo heredado del plan toma el valor que tenía por defecto.
            migrationBuilder.Sql(
                @"UPDATE TenantSignatureSettings SET
                    Plan_MaxAllowedPdfBytes = ISNULL(Plan_MaxAllowedPdfBytes, 26214400),
                    Plan_MaxAllowedPages = ISNULL(Plan_MaxAllowedPages, 100),
                    Plan_MinRetentionYears = ISNULL(Plan_MinRetentionYears, 7),
                    Plan_PurgeAllowed = ISNULL(Plan_PurgeAllowed, 0),
                    Plan_AllowedChannels = ISNULL(Plan_AllowedChannels, 3),
                    Plan_MaxTokenExpirationHours = ISNULL(Plan_MaxTokenExpirationHours, 720);"
            );

            migrationBuilder.DropTable(
                name: "SignaturePlatformSettings");

            migrationBuilder.DropTable(
                name: "TenantSignatureLimits");

            migrationBuilder.DropColumn(
                name: "Plan_MaxDocumentsPerRequest",
                table: "TenantSignatureSettings");

            migrationBuilder.DropColumn(
                name: "Plan_MaxSignersPerRequest",
                table: "TenantSignatureSettings");

            migrationBuilder.AlterColumn<bool>(
                name: "SendSealedDocumentDefault",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "Plan_PurgeAllowed",
                table: "TenantSignatureSettings",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MinRetentionYears",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MaxTokenExpirationHours",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 720,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Plan_MaxAllowedPdfBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 26214400L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_MaxAllowedPages",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 100,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Plan_AllowedChannels",
                table: "TenantSignatureSettings",
                type: "int",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Limits_MaxImageBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Plan_MaxAllowedImageBytes",
                table: "TenantSignatureSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 10485760L);
        }
    }
}
