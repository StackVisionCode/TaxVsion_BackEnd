using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactLists", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    PhoneE164 = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    CustomerRef = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptedOutChannels = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ContactListMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactListMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactListMembers_ContactLists_ContactListId",
                        column: x => x.ContactListId,
                        principalTable: "ContactLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ContactListMembers_TenantId_ContactId",
                table: "ContactListMembers",
                columns: new[] { "TenantId", "ContactId" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_ContactListMembers_ContactListId_ContactId",
                table: "ContactListMembers",
                columns: new[] { "ContactListId", "ContactId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ContactLists_TenantId_CreatedAtUtc",
                table: "ContactLists",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_TenantId_CreatedAtUtc",
                table: "Contacts",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_Contacts_TenantId_Email",
                table: "Contacts",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_Contacts_TenantId_PhoneE164",
                table: "Contacts",
                columns: new[] { "TenantId", "PhoneE164" },
                unique: true,
                filter: "[PhoneE164] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ContactListMembers");

            migrationBuilder.DropTable(name: "Contacts");

            migrationBuilder.DropTable(name: "ContactLists");
        }
    }
}
