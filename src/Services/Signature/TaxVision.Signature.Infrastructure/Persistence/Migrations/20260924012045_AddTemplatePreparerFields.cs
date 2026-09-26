using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplatePreparerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemplatePreparerFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position_Page = table.Column<int>(type: "int", nullable: false),
                    Position_X = table.Column<double>(type: "float", nullable: false),
                    Position_Y = table.Column<double>(type: "float", nullable: false),
                    Position_Width = table.Column<double>(type: "float", nullable: false),
                    Position_Height = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplatePreparerFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplatePreparerFields_SignatureTemplates_SignatureTemplateId",
                        column: x => x.SignatureTemplateId,
                        principalTable: "SignatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePreparerFields_SignatureTemplateId",
                table: "TemplatePreparerFields",
                column: "SignatureTemplateId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TemplatePreparerFields");
        }
    }
}
