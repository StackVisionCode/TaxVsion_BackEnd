using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTraceabilityAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Automatic",
                table: "WalletTopUps",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "WalletTopUps",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "MethodLabel",
                table: "WalletTopUps",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodReference",
                table: "WalletTopUps",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "WalletTopUps",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ConsumedUnits",
                table: "WalletReservations",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "WalletReservations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "EmailUnits",
                table: "WalletReservations",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "PushUnits",
                table: "WalletReservations",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "WalletReservations",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SmsUnits",
                table: "WalletReservations",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppUnits",
                table: "WalletReservations",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "LedgerEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            // Asientos viejos: el tipo sale de su OperationKey (D2). Solo escribe la columna nueva.
            migrationBuilder.Sql(TaxVision.Wallet.Infrastructure.Persistence.LedgerReferenceTypeBackfill.Sql);

            migrationBuilder.CreateTable(
                name: "TenantTimeZoneProjections",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantTimeZoneProjections", x => x.TenantId);
                }
            );

            migrationBuilder.CreateTable(
                name: "WalletSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LowBalanceThresholdMicros = table.Column<long>(type: "bigint", nullable: false),
                    LowBalanceAlertSent = table.Column<bool>(type: "bit", nullable: false),
                    AutoRechargeStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AutoRechargePausedReason = table.Column<string>(
                        type: "nvarchar(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    AutoRechargeAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    AutoRechargeThresholdMicros = table.Column<long>(type: "bigint", nullable: false),
                    AutoRechargeProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AutoRechargeMethodReference = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    AutoRechargeMethodLabel = table.Column<string>(
                        type: "nvarchar(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    ConsentAcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsentAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConsentTextVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ConsentText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RechargesDay = table.Column<DateOnly>(type: "date", nullable: true),
                    RechargesOnDay = table.Column<int>(type: "int", nullable: false),
                    DailyLimitNoticeDay = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletSettings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "WalletSettingsChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ConsentTextVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ConsentText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletSettingsChanges", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUps_TenantId_Automatic_Status",
                table: "WalletTopUps",
                columns: new[] { "TenantId", "Automatic", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_ReferenceId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "ReferenceId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WalletSettings_TenantId",
                table: "WalletSettings",
                column: "TenantId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_WalletSettingsChanges_TenantId_AtUtc",
                table: "WalletSettingsChanges",
                columns: new[] { "TenantId", "AtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantTimeZoneProjections");

            migrationBuilder.DropTable(name: "WalletSettings");

            migrationBuilder.DropTable(name: "WalletSettingsChanges");

            migrationBuilder.DropIndex(name: "IX_WalletTopUps_TenantId_Automatic_Status", table: "WalletTopUps");

            migrationBuilder.DropIndex(name: "IX_LedgerEntries_TenantId_ReferenceId", table: "LedgerEntries");

            migrationBuilder.DropColumn(name: "Automatic", table: "WalletTopUps");

            migrationBuilder.DropColumn(name: "Method", table: "WalletTopUps");

            migrationBuilder.DropColumn(name: "MethodLabel", table: "WalletTopUps");

            migrationBuilder.DropColumn(name: "PaymentMethodReference", table: "WalletTopUps");

            migrationBuilder.DropColumn(name: "Provider", table: "WalletTopUps");

            migrationBuilder.DropColumn(name: "ConsumedUnits", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "Description", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "EmailUnits", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "PushUnits", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "RequestedByUserId", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "SmsUnits", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "WhatsAppUnits", table: "WalletReservations");

            migrationBuilder.DropColumn(name: "ReferenceType", table: "LedgerEntries");
        }
    }
}
