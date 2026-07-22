using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionAllowedActorTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedActorTypes",
                table: "Permissions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000001"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000002"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000003"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000004"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000005"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000006"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000007"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000008"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000009"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000010"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000011"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000012"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000019"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000021"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000022"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000023"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000024"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000025"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000026"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000027"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000028"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000029"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000030"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000031"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000032"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000033"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000034"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000035"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000039"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000040"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000041"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000042"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000043"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000045"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000046"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000047"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000048"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000049"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000050"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000051"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000052"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000053"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000054"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000055"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000056"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000057"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000058"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000059"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000060"),
                column: "AllowedActorTypes",
                value: "CustomerPortal"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000061"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000062"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000063"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000064"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000065"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000066"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000067"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000068"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000069"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000070"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000071"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000072"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000073"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000074"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000075"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000076"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000077"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000078"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000079"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000080"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000082"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000083"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000084"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000087"),
                column: "AllowedActorTypes",
                value: "PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000088"),
                column: "AllowedActorTypes",
                value: "PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000089"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000090"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000091"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000092"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000093"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000094"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000095"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000096"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000097"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000098"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000099"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000102"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000103"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000105"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000106"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000107"),
                column: "AllowedActorTypes",
                value: "PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000108"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000109"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000110"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000111"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000113"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000114"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000115"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000116"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000117"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000118"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000119"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000120"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000121"),
                column: "AllowedActorTypes",
                value: "PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000122"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000123"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000124"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000126"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000127"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000131"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000140"),
                column: "AllowedActorTypes",
                value: "PlatformAdmin"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000141"),
                column: "AllowedActorTypes",
                value: "TenantEmployee,TenantAdmin,PlatformAdmin"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowedActorTypes", table: "Permissions");
        }
    }
}
