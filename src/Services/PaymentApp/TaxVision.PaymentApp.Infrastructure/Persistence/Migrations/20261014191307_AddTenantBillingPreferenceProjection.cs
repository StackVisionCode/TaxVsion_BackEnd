using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBillingPreferenceProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBillingPreferenceProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HasActiveMandate = table.Column<bool>(type: "bit", nullable: false),
                    AgreedAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    AgreedCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AgreedCadence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RevisionTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBillingPreferenceProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantBillingPreferenceProjections_TenantId",
                table: "TenantBillingPreferenceProjections",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantBillingPreferenceProjections");
        }
    }
}
