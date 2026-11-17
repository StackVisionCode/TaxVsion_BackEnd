using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDueItemsToRenewalIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludesPlan",
                table: "SubscriptionRenewalIntents",
                type: "bit",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.CreateTable(
                name: "SubscriptionRenewalIntentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionRenewalIntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRenewalIntentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRenewalIntentItems_SubscriptionRenewalIntents_SubscriptionRenewalIntentId",
                        column: x => x.SubscriptionRenewalIntentId,
                        principalTable: "SubscriptionRenewalIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewalIntentItems_SubscriptionRenewalIntentId",
                table: "SubscriptionRenewalIntentItems",
                column: "SubscriptionRenewalIntentId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SubscriptionRenewalIntentItems");

            migrationBuilder.DropColumn(name: "IncludesPlan", table: "SubscriptionRenewalIntents");
        }
    }
}
