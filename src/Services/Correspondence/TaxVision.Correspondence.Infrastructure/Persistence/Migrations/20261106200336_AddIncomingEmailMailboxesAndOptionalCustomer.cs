using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingEmailMailboxesAndOptionalCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "IncomingEmails",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier"
            );

            migrationBuilder.CreateTable(
                name: "IncomingEmailMailboxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomingEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingEmailMailboxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomingEmailMailboxes_IncomingEmails_IncomingEmailId",
                        column: x => x.IncomingEmailId,
                        principalTable: "IncomingEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmailMailboxes_IncomingEmailId_AccountId_Unique",
                table: "IncomingEmailMailboxes",
                columns: new[] { "IncomingEmailId", "AccountId" },
                unique: true
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_IncomingEmailMailboxes_TenantId_AccountId",
                    table: "IncomingEmailMailboxes",
                    columns: new[] { "TenantId", "AccountId" }
                )
                .Annotation("SqlServer:Include", new[] { "IncomingEmailId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IncomingEmailMailboxes");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "IncomingEmails",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );
        }
    }
}
