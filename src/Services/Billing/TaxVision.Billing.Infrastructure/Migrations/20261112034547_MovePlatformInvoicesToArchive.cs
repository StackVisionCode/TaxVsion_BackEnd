using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MovePlatformInvoicesToArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchivedPlatformInvoices",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PdfFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Total = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IssueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedPlatformInvoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedPlatformInvoices_OnboardingId",
                schema: "billing",
                table: "ArchivedPlatformInvoices",
                column: "OnboardingId",
                unique: true);

            // 1) Archivar las facturas de alta (ONB-) completas antes de sacarlas: cabecera, líneas, pagos,
            //    ajustes y cambios de estado en JSON. No se pierde ningún dato del negocio.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'
INSERT INTO [billing].[ArchivedPlatformInvoices]
    ([Id], [InvoiceNumber], [TenantId], [OnboardingId], [PaymentId], [PdfFileId], [Total], [IssueDateUtc], [SnapshotJson], [ArchivedAtUtc])
SELECT i.[Id], i.[InvoiceNumber], i.[TenantId], i.[OnboardingId], i.[PaymentId], i.[PdfFileId], i.[Total], i.[IssueDateUtc],
    (SELECT i2.*,
        (SELECT l.* FROM [billing].[InvoiceLineItems] l WHERE l.[InvoiceId] = i2.[Id] FOR JSON PATH) AS [Lines],
        (SELECT p.* FROM [billing].[InvoicePayments] p WHERE p.[InvoiceId] = i2.[Id] FOR JSON PATH) AS [Payments],
        (SELECT a.* FROM [billing].[InvoiceAdjustmentLines] a WHERE a.[InvoiceId] = i2.[Id] FOR JSON PATH) AS [Adjustments],
        (SELECT c.* FROM [billing].[InvoiceStatusChanges] c WHERE c.[InvoiceId] = i2.[Id] FOR JSON PATH) AS [StatusChanges],
        (SELECT k.* FROM [billing].[InvoicePaymentLinks] k WHERE k.[InvoiceId] = i2.[Id] FOR JSON PATH) AS [PaymentLinks]
     FROM [billing].[Invoices] i2 WHERE i2.[Id] = i.[Id]
     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
    SYSUTCDATETIME()
FROM [billing].[Invoices] i
WHERE i.[OnboardingId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [billing].[ArchivedPlatformInvoices] x WHERE x.[Id] = i.[Id]);'"
            );

            // 2) Sacarlas de Invoices (líneas, pagos, ajustes, cambios de estado y enlaces caen en cascada) y
            //    retirar la serie ONB- de la plataforma.
            migrationBuilder.Sql(
                @"EXEC sp_executesql N'DELETE FROM [billing].[Invoices] WHERE [OnboardingId] IS NOT NULL;'"
            );
            migrationBuilder.Sql(
                @"DELETE FROM [billing].[InvoiceNumberSequences] WHERE [PeriodKey] LIKE 'ONB-%';"
            );

            // 3) Billing ya no modela cobros de la plataforma.
            migrationBuilder.DropTable(
                name: "InvoiceAdjustmentLines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "OnboardingTenantMaps",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OnboardingId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OnboardingId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PlanId",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SettlementType",
                schema: "billing",
                table: "Invoices");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir no devuelve las facturas archivadas a Invoices: si hay alguna, se detiene en vez de perderlas.
            migrationBuilder.Sql(
                @"IF EXISTS (SELECT 1 FROM [billing].[ArchivedPlatformInvoices])
    THROW 50000, 'ArchivedPlatformInvoices has rows: restore them to Invoices by hand before reverting.', 1;"
            );

            migrationBuilder.DropTable(
                name: "ArchivedPlatformInvoices",
                schema: "billing");

            migrationBuilder.AddColumn<Guid>(
                name: "OnboardingId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                schema: "billing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementType",
                schema: "billing",
                table: "Invoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceAdjustmentLines",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrowthReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdjustmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAdjustmentLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingTenantMaps",
                schema: "billing",
                columns: table => new
                {
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingTenantMaps", x => x.OnboardingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OnboardingId",
                schema: "billing",
                table: "Invoices",
                column: "OnboardingId",
                unique: true,
                filter: "[OnboardingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdjustmentLines_InvoiceId",
                schema: "billing",
                table: "InvoiceAdjustmentLines",
                column: "InvoiceId");
        }
    }
}
