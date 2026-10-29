using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormsModuleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormModuleSettings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    EnabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnabledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalParticipantsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IntakeLinksEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoCustomerCreationEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SignatureStepEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PushNotificationsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormModuleSettings", x => x.TenantId);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormModuleSettings");
        }
    }
}
