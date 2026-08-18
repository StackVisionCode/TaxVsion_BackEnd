using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayableReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayableReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurposeKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalReferenceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayableReferences", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_PayableReferences_Reference",
                table: "PayableReferences",
                column: "Reference",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_PayableReferences_Tenant_Purpose_ExternalRef",
                table: "PayableReferences",
                columns: new[] { "TenantId", "PurposeKind", "ExternalReferenceId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PayableReferences");
        }
    }
}
