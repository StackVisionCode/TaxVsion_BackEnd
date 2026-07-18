using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPaymentClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CausationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeforePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAuditEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPaymentConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PublishableKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SecretKeyEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WebhookSecretEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StatementDescriptor = table.Column<string>(type: "nvarchar(22)", maxLength: 22, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentConfigs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TaxpayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurposeKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PurposeExternalReferenceId = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalChargeProvider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ExternalChargeReference = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    StatementDescriptor = table.Column<string>(type: "nvarchar(22)", maxLength: 22, nullable: false),
                    NextActionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NextActionUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChargedBackAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsLegalHeld = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPayments", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubDomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultTimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "WebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignatureHeader = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RelatedTenantPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantPaymentConfigId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SigningSecretEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWebhookEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantWebhookEndpoints_TenantPaymentConfigs_TenantPaymentConfigId",
                        column: x => x.TenantPaymentConfigId,
                        principalTable: "TenantPaymentConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RefundLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExternalRefundReference = table.Column<string>(
                        type: "nvarchar(200)",
                        maxLength: 200,
                        nullable: true
                    ),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefundLines_TenantPayments_TenantPaymentId",
                        column: x => x.TenantPaymentId,
                        principalTable: "TenantPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProviderResponseCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantPaymentAttempts_TenantPayments_TenantPaymentId",
                        column: x => x.TenantPaymentId,
                        principalTable: "TenantPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuditEntries_TenantId_Aggregate",
                table: "PaymentAuditEntries",
                columns: new[] { "TenantId", "AggregateType", "AggregateId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RefundLines_TenantPaymentId",
                table: "RefundLines",
                column: "TenantPaymentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentAttempts_TenantPaymentId",
                table: "TenantPaymentAttempts",
                column: "TenantPaymentId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantPaymentConfigs_TenantId_ProviderCode",
                table: "TenantPaymentConfigs",
                columns: new[] { "TenantId", "ProviderCode" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_Status_NextRetry",
                table: "TenantPayments",
                column: "NextRetryAtUtc",
                filter: "[Status] = 'Failed' AND [NextRetryAtUtc] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_TenantId_Status",
                table: "TenantPayments",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantPayments_TenantId_IdempotencyKey",
                table: "TenantPayments",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_SubDomain",
                table: "Tenants",
                column: "SubDomain",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantWebhookEndpoints_TenantPaymentConfigId",
                table: "TenantWebhookEndpoints",
                column: "TenantPaymentConfigId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_WebhookEvents_TenantId_ProviderCode_ProviderEventId",
                table: "WebhookEvents",
                columns: new[] { "TenantId", "ProviderCode", "ProviderEventId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PaymentAuditEntries");

            migrationBuilder.DropTable(name: "RefundLines");

            migrationBuilder.DropTable(name: "TenantPaymentAttempts");

            migrationBuilder.DropTable(name: "Tenants");

            migrationBuilder.DropTable(name: "TenantWebhookEndpoints");

            migrationBuilder.DropTable(name: "WebhookEvents");

            migrationBuilder.DropTable(name: "TenantPayments");

            migrationBuilder.DropTable(name: "TenantPaymentConfigs");
        }
    }
}
