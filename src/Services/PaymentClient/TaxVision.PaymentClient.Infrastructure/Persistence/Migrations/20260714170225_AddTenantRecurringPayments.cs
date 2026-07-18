using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRecurringPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantRecurringPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxpayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PaymentMethodReference = table.Column<string>(
                        type: "nvarchar(255)",
                        maxLength: 255,
                        nullable: false
                    ),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PurposeKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PurposeExternalReferenceId = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomIntervalDays = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxExecutions = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutionCount = table.Column<int>(type: "int", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    RetryMaxFailures = table.Column<int>(type: "int", nullable: false),
                    RetryBackoffs = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PlatformFeeAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    PlatformFeeReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRecurringPayments", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "RecurringPaymentExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantRecurringPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecurringScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ProviderResponse = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringPaymentExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringPaymentExecutions_TenantRecurringPayments_TenantRecurringPaymentId",
                        column: x => x.TenantRecurringPaymentId,
                        principalTable: "TenantRecurringPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RecurringSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantRecurringPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TenantPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    NextRetryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSchedules_TenantRecurringPayments_TenantRecurringPaymentId",
                        column: x => x.TenantRecurringPaymentId,
                        principalTable: "TenantRecurringPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPaymentExecutions_RecurringScheduleId",
                table: "RecurringPaymentExecutions",
                column: "RecurringScheduleId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPaymentExecutions_TenantRecurringPaymentId",
                table: "RecurringPaymentExecutions",
                column: "TenantRecurringPaymentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_Status_NextRetryAtUtc",
                table: "RecurringSchedules",
                columns: new[] { "Status", "NextRetryAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_Status_ScheduledDate",
                table: "RecurringSchedules",
                columns: new[] { "Status", "ScheduledDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_TenantRecurringPaymentId",
                table: "RecurringSchedules",
                column: "TenantRecurringPaymentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantRecurringPayments_TenantId_Status",
                table: "TenantRecurringPayments",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantRecurringPayments_TenantId_TaxpayerId",
                table: "TenantRecurringPayments",
                columns: new[] { "TenantId", "TaxpayerId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RecurringPaymentExecutions");

            migrationBuilder.DropTable(name: "RecurringSchedules");

            migrationBuilder.DropTable(name: "TenantRecurringPayments");
        }
    }
}
