using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreparerSignatureFileId",
                table: "SignatureRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PreparerFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignatureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Position_Page = table.Column<int>(type: "int", nullable: false),
                    Position_X = table.Column<double>(type: "float", nullable: false),
                    Position_Y = table.Column<double>(type: "float", nullable: false),
                    Position_Width = table.Column<double>(type: "float", nullable: false),
                    Position_Height = table.Column<double>(type: "float", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparerFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreparerFields_SignatureRequests_SignatureRequestId",
                        column: x => x.SignatureRequestId,
                        principalTable: "SignatureRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PreparerFields_SignatureRequestId",
                table: "PreparerFields",
                column: "SignatureRequestId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PreparerFields");

            migrationBuilder.DropColumn(name: "PreparerSignatureFileId", table: "SignatureRequests");
        }
    }
}
