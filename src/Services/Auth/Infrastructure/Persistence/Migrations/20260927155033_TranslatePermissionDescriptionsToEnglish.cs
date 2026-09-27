using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TranslatePermissionDescriptionsToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "Description",
                value: "View users"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "Description",
                value: "Invite users"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "Description",
                value: "Activate, deactivate and edit users"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "Description",
                value: "Manage roles and permissions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "Description",
                value: "View the audit log"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000006"),
                column: "Description",
                value: "Manage office settings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000007"),
                column: "Description",
                value: "View billing and subscription"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000008"),
                column: "Description",
                value: "Manage payment methods and billing"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000009"),
                column: "Description",
                value: "Change plan and manage the subscription"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000010"),
                column: "Description",
                value: "View clients"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000011"),
                column: "Description",
                value: "Create and edit clients"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000012"),
                column: "Description",
                value: "Request signatures"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                column: "Description",
                value: "View documents"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                column: "Description",
                value: "Manage documents"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                column: "Description",
                value: "Use the email module"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016"),
                column: "Description",
                value: "Make calls and hold meetings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                column: "Description",
                value: "Create and edit campaigns, contacts and lists"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                column: "Description",
                value: "View the dashboard and reports"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000019"),
                column: "Description",
                value: "The client can make calls"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
                column: "Description",
                value: "The client can use mileage tracking"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000021"),
                column: "Description",
                value: "The client can see the folders on their profile"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000023"),
                column: "Description",
                value: "View file details"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000024"),
                column: "Description",
                value: "Upload files through the secure gateway"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000025"),
                column: "Description",
                value: "Download available files"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000026"),
                column: "Description",
                value: "Delete files"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000027"),
                column: "Description",
                value: "Manage storage policies"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000028"),
                column: "Description",
                value: "View the file audit log"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000029"),
                column: "Description",
                value: "Create e-signature requests"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000030"),
                column: "Description",
                value: "View signature requests"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000031"),
                column: "Description",
                value: "Cancel signature requests"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000032"),
                column: "Description",
                value: "Resend invitations to signers"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000033"),
                column: "Description",
                value: "Extend the expiry of signature requests"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000034"),
                column: "Description",
                value: "Validate and prepare documents for signing"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000035"),
                column: "Description",
                value: "Apply the preparer's signature to a document"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                column: "Description",
                value: "View signed documents and their details"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                column: "Description",
                value: "Download the sealed file, the original or the certificate"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                column: "Description",
                value: "View a signature's audit trail"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000039"),
                column: "Description",
                value: "Create reusable signature templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000040"),
                column: "Description",
                value: "Edit signature templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000041"),
                column: "Description",
                value: "Delete signature templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000042"),
                column: "Description",
                value: "Manage the office's signature settings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000043"),
                column: "Description",
                value: "Manage the preparer's saved signatures"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                column: "Description",
                value: "Verify signature certificates (public endpoint)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000045"),
                column: "Description",
                value: "Start chat conversations"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000046"),
                column: "Description",
                value: "Reply in chat conversations"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000047"),
                column: "Description",
                value: "Moderate messages in the office's conversations"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000048"),
                column: "Description",
                value: "Open a support chat with the platform"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                column: "Description",
                value: "Handle support chats as an agent (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000050"),
                column: "Description",
                value: "Start one-to-one audio calls"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000051"),
                column: "Description",
                value: "Start one-to-one video calls"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000052"),
                column: "Description",
                value: "Record one-to-one calls (with a disclosure banner)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000053"),
                column: "Description",
                value: "Create multi-party meetings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000054"),
                column: "Description",
                value: "Join meetings with a valid invitation"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000055"),
                column: "Description",
                value: "Host meetings (waiting room, mute all, transfer)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000056"),
                column: "Description",
                value: "Record meetings (with a disclosure banner)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000057"),
                column: "Description",
                value: "Attach screenshots, voice notes and video in chat"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000058"),
                column: "Description",
                value: "Create internal groups"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000059"),
                column: "Description",
                value: "Manage internal group members"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000060"),
                column: "Description",
                value: "View your own in-app notifications"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000061"),
                column: "Description",
                value: "Manage the office's communication settings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000062"),
                column: "Description",
                value: "View the office's communication analytics"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000063"),
                column: "Description",
                value: "Reveal a client's full SSN/ITIN/EIN"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000064"),
                column: "Description",
                value: "Manage the office's own domains (custom hostnames)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000065"),
                column: "Description",
                value: "Restore and purge files from the recycle bin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000066"),
                column: "Description",
                value: "Create, rename and move folders"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000067"),
                column: "Description",
                value: "Create share links for files"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000068"),
                column: "Description",
                value: "Revoke existing share links"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000069"),
                column: "Description",
                value: "Grant elevated access on share links and manage their expiry"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000070"),
                column: "Description",
                value: "Place and lift legal holds on this office's files"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000071"),
                column: "Description",
                value: "File a DMCA counter-notice for one of this office's files"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000072"),
                column: "Description",
                value: "View the client correspondence inbox"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000073"),
                column: "Description",
                value: "Download attachments from the correspondence inbox"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000074"),
                column: "Description",
                value: "Create, edit and discard correspondence drafts"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000075"),
                column: "Description",
                value: "Reply to an incoming correspondence message"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000076"),
                column: "Description",
                value: "Send a correspondence draft that is ready to go"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000077"),
                column: "Description",
                value: "View the office's connected email accounts"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000078"),
                column: "Description",
                value: "Connect, reconnect and disconnect the office's email accounts"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000079"),
                column: "Description",
                value: "View email templates (system and this office's)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000080"),
                column: "Description",
                value: "Create, edit and publish email template versions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081"),
                column: "Description",
                value: "View email layouts (system and this office's)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000082"),
                column: "Description",
                value: "Create, edit and publish email layout versions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000083"),
                column: "Description",
                value: "View the rules that resolve an event to a template"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000084"),
                column: "Description",
                value: "Create, edit and delete the rules that resolve an event to a template"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085"),
                column: "Description",
                value: "View email campaigns built on templates (reserved, no endpoint yet)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086"),
                column: "Description",
                value: "Manage email campaigns built on templates (reserved, no endpoint yet)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087"),
                column: "Description",
                value: "Render templates (service-to-service)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000088"),
                column: "Description",
                value: "Manage an office's Signature plan limits (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000089"),
                column: "Description",
                value: "View the office's sent email history"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000090"),
                column: "Description",
                value: "View the suppression list (addresses that bounced or unsubscribed)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000091"),
                column: "Description",
                value: "Add or remove addresses from the suppression list"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000092"),
                column: "Description",
                value: "View the office's configured email provider"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000093"),
                column: "Description",
                value: "Configure the office's email provider (SMTP/API)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000094"),
                column: "Description",
                value: "Manage the office's SMTP/API notification settings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000095"),
                column: "Description",
                value: "Send a one-off email"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000096"),
                column: "Description",
                value: "View the sent email history"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000097"),
                column: "Description",
                value: "View the office's email templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000098"),
                column: "Description",
                value: "Create, edit and publish the office's email templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000099"),
                column: "Description",
                value: "Manage the office's base email layouts"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009a"),
                column: "Description",
                value: "Archive, trash, restore and permanently delete correspondence"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009b"),
                column: "Description",
                value: "Place and lift legal holds on a signature"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009c"),
                column: "Description",
                value: "Bulk import clients (CSV/Excel)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000a0"),
                column: "Description",
                value: "Email or text the signed document and certificate to the signers"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c7"),
                column: "Description",
                value: "Connect and manage your own personal mailbox"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c8"),
                column: "Description",
                value: "View the office mailbox and its mail"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c9"),
                column: "Description",
                value: "View ALL clients, not only the assigned ones"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100"),
                column: "Description",
                value: "View the office's email campaigns (reserved, no endpoint yet)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101"),
                column: "Description",
                value: "Manage the office's email campaigns (reserved, no endpoint yet)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102"),
                column: "Description",
                value: "View the notification history (email, SMS and in-app) for audit and support"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000103"),
                column: "Description",
                value: "View the office's own SaaS payments (subscription, seats and add-ons)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104"),
                column: "Description",
                value: "Refund a SaaS payment for any office (platform support)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000105"),
                column: "Description",
                value: "View the office's saved payment method"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000106"),
                column: "Description",
                value: "Manage the office's saved payment method"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000107"),
                column: "Description",
                value: "View SaaS payments for ANY office, including suspended ones (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000108"),
                column: "Description",
                value: "View the office's payment processing setup"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000109"),
                column: "Description",
                value: "Configure the office's payment processing mode and credentials"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000110"),
                column: "Description",
                value: "View the payments the office collected from its clients"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000111"),
                column: "Description",
                value: "Charge a payment to one of the office's clients"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112"),
                column: "Description",
                value: "Refund a payment collected from one of the office's clients"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000113"),
                column: "Description",
                value: "View the office's payment links"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000114"),
                column: "Description",
                value: "Create and manage the office's payment links"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000115"),
                column: "Description",
                value: "View the status of the office's Stripe Connect account"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000116"),
                column: "Description",
                value: "Start onboarding for the office's Stripe Connect account"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000117"),
                column: "Description",
                value: "View the office's scheduled payouts"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000118"),
                column: "Description",
                value: "Manage the office's payout schedule"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000119"),
                column: "Description",
                value: "View the office's recurring payments"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000120"),
                column: "Description",
                value: "Create and manage the office's recurring payments"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000121"),
                column: "Description",
                value: "View payments for ANY office, including suspended ones (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000122"),
                column: "Description",
                value: "Manage the office's logo and branding"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123"),
                column: "Description",
                value: "View the office's codes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124"),
                column: "Description",
                value: "Manage the office's codes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125"),
                column: "Description",
                value: "Issue benefit codes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126"),
                column: "Description",
                value: "Activate codes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127"),
                column: "Description",
                value: "Revoke codes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128"),
                column: "Description",
                value: "View the code audit log"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129"),
                column: "Description",
                value: "View code redemptions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130"),
                column: "Description",
                value: "Manage promotional compensation"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131"),
                column: "Description",
                value: "View your own referrals"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132"),
                column: "Description",
                value: "View referral programs"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133"),
                column: "Description",
                value: "Manage referral programs"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134"),
                column: "Description",
                value: "View referral attributions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135"),
                column: "Description",
                value: "View anti-fraud reviews"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136"),
                column: "Description",
                value: "Manage anti-fraud reviews"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137"),
                column: "Description",
                value: "View rewards"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138"),
                column: "Description",
                value: "Manage non-monetary rewards"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139"),
                column: "Description",
                value: "View the referral audit log"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000140"),
                column: "Description",
                value: "Operate Growth resources for any office (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000141"),
                column: "Description",
                value: "Assign or reassign a client's preparer"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000142"),
                column: "Description",
                value: "Manage signature requests created by other people in the office"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000143"),
                column: "Description",
                value: "Change plan, activate, cancel and manage the office's subscription"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000144"),
                column: "Description",
                value: "Suspend any office's subscription (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000145"),
                column: "Description",
                value: "Reactivate any office's subscription (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000146"),
                column: "Description",
                value: "Manually renew any office's subscription (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000147"),
                column: "Description",
                value: "View upcoming renewals, expired seats and past-due subscriptions for ANY office, and force an entitlements recalculation (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000148"),
                column: "Description",
                value: "Buy, assign, release, reassign and renew the office's seats"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000149"),
                column: "Description",
                value: "Buy, cancel and renew the office's add-ons"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000150"),
                column: "Description",
                value: "Change any office's status (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000151"),
                column: "Description",
                value: "List every office on the platform (platform only)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000152"),
                column: "Description",
                value: "Configure document branding"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000153"),
                column: "Description",
                value: "Review and resolve onboardings stuck in manual review or provisioning failure for any office"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000154"),
                column: "Description",
                value: "View notes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000155"),
                column: "Description",
                value: "Create, edit, archive, restore and attach files to your own notes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000156"),
                column: "Description",
                value: "View, archive and delete notes from anyone in the office"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000157"),
                column: "Description",
                value: "The client can read the notes marked as visible to them"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000158"),
                column: "Description",
                value: "Send SMS and MMS messages"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000159"),
                column: "Description",
                value: "View the product and service catalog"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000160"),
                column: "Description",
                value: "Create and edit products, services and categories"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000161"),
                column: "Description",
                value: "Delete products, services and categories"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000162"),
                column: "Description",
                value: "View stock, suppliers and movements"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000163"),
                column: "Description",
                value: "Manage suppliers and stock thresholds"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000164"),
                column: "Description",
                value: "Adjust stock by recording movements"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000165"),
                column: "Description",
                value: "View your reminders"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000166"),
                column: "Description",
                value: "Create, reschedule, snooze, dismiss and cancel your reminders"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000167"),
                column: "Description",
                value: "View tasks"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000168"),
                column: "Description",
                value: "Create, edit, close and reopen your own or assigned tasks"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000169"),
                column: "Description",
                value: "Assign a task to someone else in the office"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000170"),
                column: "Description",
                value: "Close, edit or reassign anyone's task (supervision)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000171"),
                column: "Description",
                value: "Create and edit the firm's task templates"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000172"),
                column: "Description",
                value: "Ask clients for documents and close what they send"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000173"),
                column: "Description",
                value: "The client sees their requests and uploads what they're asked for"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000174"),
                column: "Description",
                value: "View the calendar and check availability"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000175"),
                column: "Description",
                value: "Create, move and cancel your own appointments"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000176"),
                column: "Description",
                value: "Reorganize other people's schedules as the organizer (supervision)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000177"),
                column: "Description",
                value: "Define the firm's appointment types"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178"),
                column: "Description",
                value: "Define working hours and calendar blocks"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000179"),
                column: "Description",
                value: "Manage the platform's own brand (default colors, logo and favicon)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000180"),
                column: "Description",
                value: "View client invoices"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000181"),
                column: "Description",
                value: "Create, issue and manage client invoices"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000182"),
                column: "Description",
                value: "Record and close DMCA takedowns for any office"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000183"),
                column: "Description",
                value: "View campaigns, contacts, lists, schedules and runs"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000184"),
                column: "Description",
                value: "Send or schedule a campaign"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000185"),
                column: "Description",
                value: "Manage campaign sender profiles"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000186"),
                column: "Description",
                value: "Edit the legal issuer on the office's invoices"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f0"),
                column: "Description",
                value: "View the SMS history, its status and opt-outs"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f1"),
                column: "Description",
                value: "Manually manage SMS opt-outs and opt-ins"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "Description",
                value: "Ver usuarios del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "Description",
                value: "Invitar usuarios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "Description",
                value: "Activar, desactivar y editar usuarios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "Description",
                value: "Gestionar roles y permisos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "Description",
                value: "Consultar auditoría"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000006"),
                column: "Description",
                value: "Gestionar configuración del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000007"),
                column: "Description",
                value: "Ver facturación y suscripción"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000008"),
                column: "Description",
                value: "Gestionar métodos de pago y facturación"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000009"),
                column: "Description",
                value: "Cambiar plan y gestionar suscripción"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000010"),
                column: "Description",
                value: "Ver clientes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000011"),
                column: "Description",
                value: "Crear y editar clientes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000012"),
                column: "Description",
                value: "Solicitar firmas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                column: "Description",
                value: "Ver documentos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                column: "Description",
                value: "Gestionar documentos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                column: "Description",
                value: "Usar el módulo de correo"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016"),
                column: "Description",
                value: "Realizar llamadas y meetings"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                column: "Description",
                value: "Crear y editar campañas, contactos y listas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                column: "Description",
                value: "Ver dashboard y reportes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000019"),
                column: "Description",
                value: "El cliente puede realizar llamadas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
                column: "Description",
                value: "El cliente puede usar el módulo de millas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000021"),
                column: "Description",
                value: "El cliente puede ver folders de su perfil"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000023"),
                column: "Description",
                value: "Ver metadatos de archivos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000024"),
                column: "Description",
                value: "Subir archivos mediante el gateway seguro"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000025"),
                column: "Description",
                value: "Descargar archivos disponibles"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000026"),
                column: "Description",
                value: "Eliminar archivos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000027"),
                column: "Description",
                value: "Gestionar políticas de almacenamiento"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000028"),
                column: "Description",
                value: "Consultar auditoría de archivos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000029"),
                column: "Description",
                value: "Crear solicitudes de firma electrónica"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000030"),
                column: "Description",
                value: "Consultar solicitudes de firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000031"),
                column: "Description",
                value: "Cancelar solicitudes de firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000032"),
                column: "Description",
                value: "Reenviar invitaciones a firmantes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000033"),
                column: "Description",
                value: "Extender el vencimiento de solicitudes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000034"),
                column: "Description",
                value: "Validar y preparar documentos para firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000035"),
                column: "Description",
                value: "Aplicar firma del preparador al documento"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                column: "Description",
                value: "Ver documentos firmados y sus metadatos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                column: "Description",
                value: "Descargar sellado, original o certificado"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                column: "Description",
                value: "Consultar el audit trail de una firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000039"),
                column: "Description",
                value: "Crear plantillas de firma reutilizables"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000040"),
                column: "Description",
                value: "Modificar plantillas de firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000041"),
                column: "Description",
                value: "Eliminar plantillas de firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000042"),
                column: "Description",
                value: "Gestionar la configuración de firma del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000043"),
                column: "Description",
                value: "Gestionar firmas persistentes del preparador"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                column: "Description",
                value: "Verificar certificados de firma (endpoint público)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000045"),
                column: "Description",
                value: "Iniciar conversaciones de chat"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000046"),
                column: "Description",
                value: "Responder en conversaciones de chat"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000047"),
                column: "Description",
                value: "Moderar mensajes en conversaciones del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000048"),
                column: "Description",
                value: "Abrir chat de soporte hacia el PlatformTenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                column: "Description",
                value: "Atender chats de soporte como agente (PlatformTenant)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000050"),
                column: "Description",
                value: "Iniciar llamadas de audio 1:1"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000051"),
                column: "Description",
                value: "Iniciar llamadas de video 1:1"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000052"),
                column: "Description",
                value: "Grabar llamadas 1:1 (con banner de disclosure)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000053"),
                column: "Description",
                value: "Crear reuniones multi-party"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000054"),
                column: "Description",
                value: "Unirse a reuniones (previa invitación válida)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000055"),
                column: "Description",
                value: "Actuar como host de reuniones (waiting room, mute all, transfer)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000056"),
                column: "Description",
                value: "Grabar reuniones (con banner de disclosure)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000057"),
                column: "Description",
                value: "Adjuntar screenshots/voice/video en chat"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000058"),
                column: "Description",
                value: "Crear grupos internos por tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000059"),
                column: "Description",
                value: "Gestionar miembros de grupos internos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000060"),
                column: "Description",
                value: "Consultar notificaciones in-app propias"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000061"),
                column: "Description",
                value: "Gestionar la configuración de Communication del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000062"),
                column: "Description",
                value: "Consultar analytics de Communication del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000063"),
                column: "Description",
                value: "Revelar el SSN/ITIN/EIN completo de un customer"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000064"),
                column: "Description",
                value: "Gestionar dominios propios del tenant (custom hostnames)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000065"),
                column: "Description",
                value: "Restaurar y purgar archivos de la papelera"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000066"),
                column: "Description",
                value: "Crear, renombrar y mover carpetas de archivos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000067"),
                column: "Description",
                value: "Crear links para compartir archivos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000068"),
                column: "Description",
                value: "Revocar links de compartir existentes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000069"),
                column: "Description",
                value: "Otorgar permisos elevados en links y gestionar su expiracion"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000070"),
                column: "Description",
                value: "Gestionar legal hold sobre archivos del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000071"),
                column: "Description",
                value: "Presentar contranotificacion DMCA sobre un archivo propio"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000072"),
                column: "Description",
                value: "Ver la bandeja de correspondencia con customers"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000073"),
                column: "Description",
                value: "Descargar adjuntos de la bandeja de correspondencia"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000074"),
                column: "Description",
                value: "Crear, editar y descartar borradores de correspondencia"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000075"),
                column: "Description",
                value: "Responder a un mensaje entrante de correspondencia"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000076"),
                column: "Description",
                value: "Enviar un borrador de correspondencia ya redactado"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000077"),
                column: "Description",
                value: "Ver las cuentas de correo conectadas del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000078"),
                column: "Description",
                value: "Conectar, reconectar y desconectar cuentas de correo del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000079"),
                column: "Description",
                value: "Ver templates de correo (System y del tenant)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000080"),
                column: "Description",
                value: "Crear, editar y publicar versiones de templates de correo"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081"),
                column: "Description",
                value: "Ver layouts de correo (System y del tenant)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000082"),
                column: "Description",
                value: "Crear, editar y publicar versiones de layouts de correo"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000083"),
                column: "Description",
                value: "Ver las reglas de resolución evento→template"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000084"),
                column: "Description",
                value: "Crear, editar y borrar reglas de resolución evento→template"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085"),
                column: "Description",
                value: "Ver campañas de correo basadas en templates de Scribe (reservado, sin controller aún)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086"),
                column: "Description",
                value: "Gestionar campañas de correo basadas en templates de Scribe (reservado, sin controller aún)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087"),
                column: "Description",
                value: "Invocar el render de templates (M2M — Notification u otros servicios via token de servicio)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000088"),
                column: "Description",
                value: "Gestionar los techos de plan de Signature de un tenant (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000089"),
                column: "Description",
                value: "Ver el historial de correos enviados del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000090"),
                column: "Description",
                value: "Ver la suppression list (direcciones que rebotaron o se dieron de baja) del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000091"),
                column: "Description",
                value: "Agregar o quitar direcciones de la suppression list del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000092"),
                column: "Description",
                value: "Ver el proveedor de correo configurado para el tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000093"),
                column: "Description",
                value: "Configurar el proveedor de correo (SMTP/API) del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000094"),
                column: "Description",
                value: "Gestionar la configuración SMTP/API de Notification del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000095"),
                column: "Description",
                value: "Enviar un correo puntual desde Notification"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000096"),
                column: "Description",
                value: "Ver el historial de correos enviados desde Notification"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000097"),
                column: "Description",
                value: "Ver los templates de correo del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000098"),
                column: "Description",
                value: "Crear, editar y publicar templates de correo del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000099"),
                column: "Description",
                value: "Gestionar los layouts base de correo del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009a"),
                column: "Description",
                value: "Archivar, enviar a papelera, restaurar y borrar definitivamente correspondencia"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009b"),
                column: "Description",
                value: "Colocar y levantar retención legal (legal hold) sobre una firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-00000000009c"),
                column: "Description",
                value: "Importar clientes en bloque (CSV/Excel)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000a0"),
                column: "Description",
                value: "Entregar por email/SMS el documento firmado y el certificado a los firmantes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c7"),
                column: "Description",
                value: "Conectar y administrar el buzón de correo personal propio"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c8"),
                column: "Description",
                value: "Ver el buzón de correo de oficina y su correo"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000000c9"),
                column: "Description",
                value: "Ver TODOS los clientes del tenant (no solo los asignados)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100"),
                column: "Description",
                value: "Ver campañas de correo del tenant (reservado, sin controller aún)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101"),
                column: "Description",
                value: "Gestionar campañas de correo del tenant (reservado, sin controller aún)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102"),
                column: "Description",
                value: "Ver el historial de notificaciones del tenant (email/SMS/in-app) para auditoría y soporte"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000103"),
                column: "Description",
                value: "Ver los pagos SaaS (suscripción/seats/add-ons) del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104"),
                column: "Description",
                value: "Reembolsar un pago SaaS de cualquier tenant (soporte de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000105"),
                column: "Description",
                value: "Ver el método de pago guardado (provider customer) del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000106"),
                column: "Description",
                value: "Gestionar el método de pago guardado del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000107"),
                column: "Description",
                value: "Ver pagos SaaS de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000108"),
                column: "Description",
                value: "Ver la configuración de cobro (Stripe DirectApiKeys/Connect) del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000109"),
                column: "Description",
                value: "Configurar el modo/credenciales de cobro del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000110"),
                column: "Description",
                value: "Ver los pagos que el tenant cobró a sus propios clientes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000111"),
                column: "Description",
                value: "Cobrar un pago a un cliente del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112"),
                column: "Description",
                value: "Reembolsar un pago cobrado a un cliente del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000113"),
                column: "Description",
                value: "Ver los links de pago del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000114"),
                column: "Description",
                value: "Crear y gestionar links de pago del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000115"),
                column: "Description",
                value: "Ver el estado de la cuenta Stripe Connect del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000116"),
                column: "Description",
                value: "Iniciar el onboarding de la cuenta Stripe Connect del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000117"),
                column: "Description",
                value: "Ver los payouts programados del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000118"),
                column: "Description",
                value: "Gestionar el calendario de payouts del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000119"),
                column: "Description",
                value: "Ver los pagos recurrentes configurados del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000120"),
                column: "Description",
                value: "Crear y gestionar pagos recurrentes del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000121"),
                column: "Description",
                value: "Ver pagos de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000122"),
                column: "Description",
                value: "Gestionar el logo/branding del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123"),
                column: "Description",
                value: "Ver códigos del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124"),
                column: "Description",
                value: "Gestionar códigos del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125"),
                column: "Description",
                value: "Emitir códigos de beneficio"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126"),
                column: "Description",
                value: "Activar códigos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127"),
                column: "Description",
                value: "Revocar códigos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128"),
                column: "Description",
                value: "Consultar auditoría de códigos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129"),
                column: "Description",
                value: "Consultar redemptions"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130"),
                column: "Description",
                value: "Gestionar compensaciones promocionales"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131"),
                column: "Description",
                value: "Ver referidos propios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132"),
                column: "Description",
                value: "Ver programas de referidos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133"),
                column: "Description",
                value: "Gestionar programas de referidos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134"),
                column: "Description",
                value: "Consultar atribuciones"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135"),
                column: "Description",
                value: "Consultar revisiones antifraude"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136"),
                column: "Description",
                value: "Gestionar revisiones antifraude"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137"),
                column: "Description",
                value: "Consultar rewards"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138"),
                column: "Description",
                value: "Gestionar rewards no monetarios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139"),
                column: "Description",
                value: "Consultar auditoría de referidos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000140"),
                column: "Description",
                value: "Operar recursos Growth de cualquier tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000141"),
                column: "Description",
                value: "Asignar o reasignar el preparador responsable de un customer"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000142"),
                column: "Description",
                value: "Gestionar solicitudes de firma creadas por otros usuarios del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000143"),
                column: "Description",
                value: "Cambiar plan, activar, cancelar y gestionar el ciclo de vida de la suscripción del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000144"),
                column: "Description",
                value: "Suspender la suscripción de cualquier tenant (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000145"),
                column: "Description",
                value: "Reactivar la suscripción de cualquier tenant (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000146"),
                column: "Description",
                value: "Renovación manual de la suscripción de cualquier tenant, mientras no exista Billing (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000147"),
                column: "Description",
                value: "Consultar renovaciones próximas, seats vencidos y suscripciones en mora de CUALQUIER tenant, y forzar el recálculo de entitlements (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000148"),
                column: "Description",
                value: "Comprar, asignar, liberar, reasignar y renovar seats del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000149"),
                column: "Description",
                value: "Comprar, cancelar y renovar add-ons del propio tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000150"),
                column: "Description",
                value: "Cambiar el estado de cualquier tenant (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000151"),
                column: "Description",
                value: "Listar todos los tenants de la plataforma (uso exclusivo de plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000152"),
                column: "Description",
                value: "Configurar el branding de documentos del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000153"),
                column: "Description",
                value: "Ver y administrar onboardings de PayFlow en ManualReview/ProvisioningFailed de cualquier tenant (resume, corrección, force-complete, cancelar y reembolsar)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000154"),
                column: "Description",
                value: "Ver notas del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000155"),
                column: "Description",
                value: "Crear, editar, archivar/restaurar y adjuntar archivos a notas propias"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000156"),
                column: "Description",
                value: "Ver, archivar y borrar notas de cualquier autor del tenant (gobernanza)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000157"),
                column: "Description",
                value: "El cliente puede ver sus notas marcadas como visibles para el cliente"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000158"),
                column: "Description",
                value: "Enviar SMS/MMS (batch 1..N) vía el microservicio SMS"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000159"),
                column: "Description",
                value: "Ver el catálogo de productos/servicios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000160"),
                column: "Description",
                value: "Crear/editar productos, servicios y categorías"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000161"),
                column: "Description",
                value: "Borrar productos, servicios y categorías"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000162"),
                column: "Description",
                value: "Ver stock, proveedores y movimientos"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000163"),
                column: "Description",
                value: "Gestionar proveedores y umbrales de stock"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000164"),
                column: "Description",
                value: "Ajustar stock (registrar movimientos)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000165"),
                column: "Description",
                value: "Ver los recordatorios propios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000166"),
                column: "Description",
                value: "Crear, reprogramar, posponer, descartar y cancelar recordatorios propios"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000167"),
                column: "Description",
                value: "Ver las tareas del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000168"),
                column: "Description",
                value: "Crear, editar, cerrar y reabrir tareas propias o asignadas a uno mismo"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000169"),
                column: "Description",
                value: "Asignar una tarea a otra persona del tenant (sin restricción de dirección)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000170"),
                column: "Description",
                value: "Cerrar, editar o reasignar la tarea de cualquier usuario del tenant (supervisión)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000171"),
                column: "Description",
                value: "Crear y editar las plantillas de tarea de la firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000172"),
                column: "Description",
                value: "Pedirle documentacion al cliente y cerrar lo que mande"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000173"),
                column: "Description",
                value: "El cliente ve sus pedidos y registra lo que sube"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000174"),
                column: "Description",
                value: "Ver el calendario del tenant y consultar disponibilidad"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000175"),
                column: "Description",
                value: "Crear, mover y cancelar las citas propias"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000176"),
                column: "Description",
                value: "Reorganizar agendas ajenas actuando como organizador (supervision)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000177"),
                column: "Description",
                value: "Definir los tipos de cita de la firma"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178"),
                column: "Description",
                value: "Definir horarios de atencion y bloqueos de agenda"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000179"),
                column: "Description",
                value: "Gestionar la marca del sistema (colores/logo/favicon por defecto de la plataforma)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000180"),
                column: "Description",
                value: "Ver facturas de clientes del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000181"),
                column: "Description",
                value: "Crear, emitir y gestionar facturas de clientes"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000182"),
                column: "Description",
                value: "Registrar y cerrar takedowns DMCA de cualquier tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000183"),
                column: "Description",
                value: "Ver campañas, contactos, listas, programaciones y corridas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000184"),
                column: "Description",
                value: "Disparar o programar el envío de una campaña"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000185"),
                column: "Description",
                value: "Administrar los perfiles de remitente de las campañas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000186"),
                column: "Description",
                value: "Editar el emisor legal de las facturas del tenant"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f0"),
                column: "Description",
                value: "Ver el historial de SMS, su estado y las bajas (opt-outs)"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-0000000001f1"),
                column: "Description",
                value: "Gestionar manualmente las bajas de SMS (opt-out/opt-in)"
            );
        }
    }
}
