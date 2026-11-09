using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Sms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsMessageRowVersionAndPendingReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "smsMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.CreateTable(
                name: "smsPendingDeliveryReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CanonicalStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_smsPendingDeliveryReceipts", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SmsPendingDeliveryReceipts_ReceivedAt",
                table: "smsPendingDeliveryReceipts",
                column: "ReceivedAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "UX_SmsPendingDeliveryReceipts_Provider_MessageId_EventType",
                table: "smsPendingDeliveryReceipts",
                columns: new[] { "ProviderCode", "ProviderMessageId", "EventType" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "smsPendingDeliveryReceipts");

            migrationBuilder.DropColumn(name: "RowVersion", table: "smsMessages");
        }
    }
}
