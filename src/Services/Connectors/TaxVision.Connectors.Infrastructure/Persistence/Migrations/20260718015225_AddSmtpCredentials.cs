using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmtpCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    UseStartTls = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PasswordCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordNonce = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordTag = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PasswordKeyVersion = table.Column<short>(type: "smallint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpCredentials", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SmtpCredentials_AccountId",
                table: "SmtpCredentials",
                column: "AccountId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SmtpCredentials");
        }
    }
}
