using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentMessagesDeliveryLaneIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_DeliveryLane_SentAtUtc",
                table: "SentMessages",
                columns: new[] { "DeliveryLane", "SentAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SentMessages_DeliveryLane_SentAtUtc", table: "SentMessages");
        }
    }
}
