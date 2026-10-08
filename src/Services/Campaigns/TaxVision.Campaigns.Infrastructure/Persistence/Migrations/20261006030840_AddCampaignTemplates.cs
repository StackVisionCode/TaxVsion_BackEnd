using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channels = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignTemplates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CampaignTemplateContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 500000, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignTemplateContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignTemplateContents_CampaignTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "CampaignTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_CampaignTemplateContents_TemplateId_Channel",
                table: "CampaignTemplateContents",
                columns: new[] { "TemplateId", "Channel" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTemplates_TenantId_CreatedAtUtc",
                table: "CampaignTemplates",
                columns: new[] { "TenantId", "CreatedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CampaignTemplateContents");

            migrationBuilder.DropTable(name: "CampaignTemplates");
        }
    }
}
