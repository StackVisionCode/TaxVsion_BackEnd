using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendSentMessageForDirectSend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "SentMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<Guid>(
                name: "CorrespondenceDraftId",
                table: "SentMessages",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "InReplyToInternetMessageId",
                table: "SentMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderThreadId",
                table: "SentMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReferencesJson",
                table: "SentMessages",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_CorrespondenceDraftId",
                table: "SentMessages",
                columns: new[] { "TenantId", "CorrespondenceDraftId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SentMessages_TenantId_CorrespondenceDraftId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "AttachmentsJson", table: "SentMessages");

            migrationBuilder.DropColumn(name: "CorrespondenceDraftId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "InReplyToInternetMessageId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "ProviderThreadId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "ReferencesJson", table: "SentMessages");
        }
    }
}
