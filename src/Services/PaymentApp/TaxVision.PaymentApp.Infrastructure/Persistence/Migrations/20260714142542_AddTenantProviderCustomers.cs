using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProviderCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantProviderCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerReferenceProvider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerReferenceValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantProviderCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantProviderCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MethodReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ExpMonth = table.Column<int>(type: "int", nullable: false),
                    ExpYear = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDetached = table.Column<bool>(type: "bit", nullable: false),
                    DetachedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSavedPaymentMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSavedPaymentMethods_TenantProviderCustomers_TenantProviderCustomerId",
                        column: x => x.TenantProviderCustomerId,
                        principalTable: "TenantProviderCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_TenantProviderCustomers_TenantId_ProviderCode",
                table: "TenantProviderCustomers",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSavedPaymentMethods_Expiration",
                table: "TenantSavedPaymentMethods",
                columns: new[] { "ExpYear", "ExpMonth" },
                filter: "[IsDetached] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TenantSavedPaymentMethods_Customer_MethodReference_Active",
                table: "TenantSavedPaymentMethods",
                columns: new[] { "TenantProviderCustomerId", "MethodReference" },
                unique: true,
                filter: "[IsDetached] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSavedPaymentMethods");

            migrationBuilder.DropTable(
                name: "TenantProviderCustomers");
        }
    }
}
