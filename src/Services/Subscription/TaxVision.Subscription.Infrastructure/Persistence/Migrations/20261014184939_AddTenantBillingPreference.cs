using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBillingPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBillingPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChosenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChosenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBillingPreferences", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPaymentMandates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantBillingPreferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConsentText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ConsentTextVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConsentIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConsentUserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AgreedAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    AgreedCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AgreedCadence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentMandates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantPaymentMandates_TenantBillingPreferences_TenantBillingPreferenceId",
                        column: x => x.TenantBillingPreferenceId,
                        principalTable: "TenantBillingPreferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantBillingPreferences_TenantId",
                table: "TenantBillingPreferences",
                column: "TenantId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMandates_Tenant_IsRevoked",
                table: "TenantPaymentMandates",
                columns: new[] { "TenantId", "IsRevoked" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMandates_TenantBillingPreferenceId",
                table: "TenantPaymentMandates",
                column: "TenantBillingPreferenceId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantPaymentMandates");

            migrationBuilder.DropTable(name: "TenantBillingPreferences");
        }
    }
}
