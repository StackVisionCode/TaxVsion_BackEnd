using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Scribe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "LayoutVersionNumber",
                table: "EmailTemplateVersions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LayoutId",
                table: "EmailTemplateVersions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier"
            );

            migrationBuilder.AlterColumn<string>(
                name: "HtmlStorageKey",
                table: "EmailTemplateVersions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "HtmlFileId",
                table: "EmailTemplateVersions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier"
            );

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "EmailTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Email"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Channel", table: "EmailTemplates");

            migrationBuilder.AlterColumn<int>(
                name: "LayoutVersionNumber",
                table: "EmailTemplateVersions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "LayoutId",
                table: "EmailTemplateVersions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "HtmlStorageKey",
                table: "EmailTemplateVersions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "HtmlFileId",
                table: "EmailTemplateVersions",
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
