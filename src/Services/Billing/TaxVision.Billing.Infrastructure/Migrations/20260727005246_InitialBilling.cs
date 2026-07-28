using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "InvoiceNumberSequences",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Next = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IssueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Customer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subtotal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TaxTotal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DiscountTotal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Total = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AmountPaid = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AmountDue = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PoNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PdfFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaidPdfFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLineItems",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitAmount = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TaxBasisPoints = table.Column<int>(type: "int", nullable: false),
                    TaxAmount = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LineTotal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLineItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineItems_InvoiceId",
                schema: "billing",
                table: "InvoiceLineItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceNumberSequences_TenantId_PeriodKey",
                schema: "billing",
                table: "InvoiceNumberSequences",
                columns: new[] { "TenantId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_InvoiceNumber",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true,
                filter: "[InvoiceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Status",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLineItems",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "InvoiceNumberSequences",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "billing");
        }
    }
}
