using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Sms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processedWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processedWebhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "smsMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    To = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceContext = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_smsMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "smsOptOuts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneE164 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastKeyword = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OptedOutAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OptedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_smsOptOuts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "smsMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SmsMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ProviderMediaId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_smsMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_smsMedia_smsMessages_SmsMessageId",
                        column: x => x.SmsMessageId,
                        principalTable: "smsMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProcessedWebhooks_Provider_MessageId_EventType",
                table: "processedWebhooks",
                columns: new[] { "ProviderCode", "ProviderMessageId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsMedia_SmsMessageId",
                table: "smsMedia",
                column: "SmsMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_BatchId",
                table: "smsMessages",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_CorrelationId",
                table: "smsMessages",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Provider_MessageId",
                table: "smsMessages",
                columns: new[] { "ProviderCode", "ProviderMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Tenant_Customer_Created",
                table: "smsMessages",
                columns: new[] { "TenantId", "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_To",
                table: "smsMessages",
                column: "To");

            migrationBuilder.CreateIndex(
                name: "UX_SmsMessages_Tenant_Idempotency",
                table: "smsMessages",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsOptOuts_Tenant_Phone",
                table: "smsOptOuts",
                columns: new[] { "TenantId", "PhoneE164" });

            migrationBuilder.CreateIndex(
                name: "UX_SmsOptOuts_Tenant_Customer_Phone",
                table: "smsOptOuts",
                columns: new[] { "TenantId", "CustomerId", "PhoneE164" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processedWebhooks");

            migrationBuilder.DropTable(
                name: "smsMedia");

            migrationBuilder.DropTable(
                name: "smsOptOuts");

            migrationBuilder.DropTable(
                name: "smsMessages");
        }
    }
}
