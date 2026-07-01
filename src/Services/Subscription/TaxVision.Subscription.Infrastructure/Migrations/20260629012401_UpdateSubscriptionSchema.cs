using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Subscription.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSubscriptionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceLevel",
                table: "Plans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Plans",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingPlanChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OldPlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewPlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OldBillingPeriod = table.Column<int>(type: "int", nullable: true),
                    NewBillingPeriod = table.Column<int>(type: "int", nullable: true),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OldUserLimit = table.Column<int>(type: "int", nullable: true),
                    NewUserLimit = table.Column<int>(type: "int", nullable: true),
                    AdditionalUsers = table.Column<int>(type: "int", nullable: true),
                    ModulesAffected = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProratedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewRecurringAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsApplied = table.Column<bool>(type: "bit", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OldRenewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewRenewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemainingDays = table.Column<int>(type: "int", nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPlanChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingPlanChanges_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingItemTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingItemTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RenewalNotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalNotificationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsIncluded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantModules_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanModules",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModules", x => new { x.PlanId, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_PlanModules_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanModules_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MinQuantity = table.Column<int>(type: "int", nullable: true),
                    MaxQuantity = table.Column<int>(type: "int", nullable: true),
                    AnnualPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PricingItemTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsRecurring = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingItems_PricingItemTypes_PricingItemTypeId",
                        column: x => x.PricingItemTypeId,
                        principalTable: "PricingItemTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentMethodId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentProviderId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DiscountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_TransactionStatuses_TransactionStatusId",
                        column: x => x.TransactionStatusId,
                        principalTable: "TransactionStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_TransactionTypes_TransactionTypeId",
                        column: x => x.TransactionTypeId,
                        principalTable: "TransactionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingHistories_PricingItems_PricingItemId",
                        column: x => x.PricingItemId,
                        principalTable: "PricingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsProrated = table.Column<bool>(type: "bit", nullable: false),
                    ProrationStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProrationEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalDaysInPeriod = table.Column<int>(type: "int", nullable: true),
                    RemainingDays = table.Column<int>(type: "int", nullable: true),
                    ProrationFactor = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionHistories_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PricingItemTypes",
                columns: new[] { "Id", "Code", "Description", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "USER_LIMIT", "Precio por usuario adicional según tier", true, "Límite de Usuarios", 1 },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "MODULE", "Módulos adicionales del sistema", true, "Módulo", 2 },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "STORAGE", "Almacenamiento adicional en la nube", true, "Almacenamiento", 3 },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "SUPPORT", "Planes de soporte adicionales", true, "Soporte", 4 },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "FEATURE", "Características y funcionalidades adicionales", true, "Característica", 5 }
                });

            migrationBuilder.InsertData(
                table: "TransactionStatuses",
                columns: new[] { "Id", "Code", "Color", "Description", "IsActive", "IsFinal", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), "PENDING", "warning", "Transacción creada, pendiente de procesamiento", true, false, "Pendiente", 1 },
                    { new Guid("30000000-0000-0000-0000-000000000002"), "PROCESSING", "info", "Transacción en proceso de pago", true, false, "Procesando", 2 },
                    { new Guid("30000000-0000-0000-0000-000000000003"), "COMPLETED", "success", "Transacción completada exitosamente", true, true, "Completada", 3 },
                    { new Guid("30000000-0000-0000-0000-000000000004"), "FAILED", "danger", "Transacción fallida", true, true, "Fallida", 4 },
                    { new Guid("30000000-0000-0000-0000-000000000005"), "REFUNDED", "secondary", "Transacción reembolsada completamente", true, true, "Reembolsada", 5 },
                    { new Guid("30000000-0000-0000-0000-000000000006"), "CANCELLED", "secondary", "Transacción cancelada", true, true, "Cancelada", 6 },
                    { new Guid("30000000-0000-0000-0000-000000000007"), "PARTIAL_REFUND", "warning", "Transacción con reembolso parcial", true, false, "Reembolso Parcial", 7 }
                });

            migrationBuilder.InsertData(
                table: "TransactionTypes",
                columns: new[] { "Id", "Code", "Description", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), "SUBSCRIPTION", "Pago de suscripción inicial", true, "Suscripción Inicial", 1 },
                    { new Guid("20000000-0000-0000-0000-000000000002"), "RENEWAL", "Renovación de suscripción", true, "Renovación", 2 },
                    { new Guid("20000000-0000-0000-0000-000000000003"), "USER_INCREASE", "Aumento de límite de usuarios", true, "Aumento de Usuarios", 3 },
                    { new Guid("20000000-0000-0000-0000-000000000004"), "MODULE_ADD", "Adición de módulo adicional", true, "Adición de Módulo", 4 },
                    { new Guid("20000000-0000-0000-0000-000000000005"), "UPGRADE", "Actualización a un plan superior", true, "Upgrade de Plan", 5 },
                    { new Guid("20000000-0000-0000-0000-000000000006"), "DOWNGRADE", "Cambio a un plan inferior", true, "Downgrade de Plan", 6 },
                    { new Guid("20000000-0000-0000-0000-000000000007"), "ONE_TIME", "Cargo único o extraordinario", true, "Cargo Único", 7 },
                    { new Guid("20000000-0000-0000-0000-000000000008"), "REFUND", "Reembolso de pago", true, "Reembolso", 8 },
                    { new Guid("20000000-0000-0000-0000-000000000009"), "PRORATED", "Ajuste prorrateado por cambios en el plan", true, "Ajuste Prorrateado", 9 },
                    { new Guid("20000000-0000-0000-0000-000000000010"), "BILLING_PERIOD_CHANGE", "Cambio de período de facturación (mensual ↔ anual)", true, "Cambio de Período", 10 },
                    { new Guid("20000000-0000-0000-0000-000000000011"), "PLAN_UPGRADE_WITH_PERIOD_CHANGE", "Actualización de plan y cambio de período simultáneos", true, "Upgrade con Cambio de Período", 11 },
                    { new Guid("20000000-0000-0000-0000-000000000012"), "PLAN_MODIFICATION", "Modificación general del plan", true, "Modificación de Plan", 12 }
                });

            migrationBuilder.InsertData(
                table: "PricingItems",
                columns: new[] { "Id", "AnnualPrice", "Description", "IsActive", "IsRecurring", "ItemCode", "MaxQuantity", "Metadata", "MinQuantity", "Name", "PricingItemTypeId", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), 400m, "Precio por usuario para planes de 1-5 usuarios", true, true, "USER_LIMIT_STARTER", 5, null, 1, "Usuario - Tier Starter", new Guid("10000000-0000-0000-0000-000000000001"), 1 },
                    { new Guid("11111111-1111-1111-1111-111111111112"), 350m, "Precio por usuario para planes de 6-20 usuarios", true, true, "USER_LIMIT_BUSINESS", 20, null, 6, "Usuario - Tier Business", new Guid("10000000-0000-0000-0000-000000000001"), 2 },
                    { new Guid("11111111-1111-1111-1111-111111111113"), 300m, "Precio por usuario para planes de 21-50 usuarios", true, true, "USER_LIMIT_PROFESSIONAL", 50, null, 21, "Usuario - Tier Professional", new Guid("10000000-0000-0000-0000-000000000001"), 3 },
                    { new Guid("11111111-1111-1111-1111-111111111114"), 250m, "Precio por usuario para planes de 51-100 usuarios", true, true, "USER_LIMIT_ENTERPRISE", 100, null, 51, "Usuario - Tier Enterprise", new Guid("10000000-0000-0000-0000-000000000001"), 4 },
                    { new Guid("11111111-1111-1111-1111-111111111115"), 200m, "Precio por usuario para planes de 101+ usuarios", true, true, "USER_LIMIT_ENTERPRISE_PLUS", 999999, null, 101, "Usuario - Tier Enterprise Plus", new Guid("10000000-0000-0000-0000-000000000001"), 5 },
                    { new Guid("22222222-2222-2222-2222-222222222221"), 1200m, "Sistema avanzado de gestión de inventario con trazabilidad completa", true, true, "MODULE_INVENTORY", null, null, null, "Módulo de Inventario", new Guid("10000000-0000-0000-0000-000000000002"), 10 },
                    { new Guid("22222222-2222-2222-2222-222222222222"), 1500m, "Sistema de gestión de relaciones con clientes", true, true, "MODULE_CRM", null, null, null, "Módulo CRM", new Guid("10000000-0000-0000-0000-000000000002"), 11 },
                    { new Guid("22222222-2222-2222-2222-222222222223"), 1800m, "Sistema contable integrado con reportes financieros", true, true, "MODULE_ACCOUNTING", null, null, null, "Módulo de Contabilidad", new Guid("10000000-0000-0000-0000-000000000002"), 12 },
                    { new Guid("22222222-2222-2222-2222-222222222224"), 1000m, "Sistema de gestión de recursos humanos y nómina", true, true, "MODULE_HR", null, null, null, "Módulo de Recursos Humanos", new Guid("10000000-0000-0000-0000-000000000002"), 13 },
                    { new Guid("22222222-2222-2222-2222-222222222225"), 2000m, "Dashboards y reportes avanzados con Business Intelligence", true, true, "MODULE_ANALYTICS", null, null, null, "Módulo de Analítica Avanzada", new Guid("10000000-0000-0000-0000-000000000002"), 14 },
                    { new Guid("22222222-2222-2222-2222-222222222226"), 2500m, "Plataforma de ventas online integrada", true, true, "MODULE_ECOMMERCE", null, null, null, "Módulo de E-Commerce", new Guid("10000000-0000-0000-0000-000000000002"), 15 },
                    { new Guid("33333333-3333-3333-3333-333333333331"), 120m, "50GB de almacenamiento adicional en la nube", true, true, "STORAGE_50GB", null, null, null, "Almacenamiento 50GB", new Guid("10000000-0000-0000-0000-000000000003"), 20 },
                    { new Guid("33333333-3333-3333-3333-333333333332"), 240m, "100GB de almacenamiento adicional en la nube", true, true, "STORAGE_100GB", null, null, null, "Almacenamiento 100GB", new Guid("10000000-0000-0000-0000-000000000003"), 21 },
                    { new Guid("33333333-3333-3333-3333-333333333333"), 1000m, "500GB de almacenamiento adicional en la nube", true, true, "STORAGE_500GB", null, null, null, "Almacenamiento 500GB", new Guid("10000000-0000-0000-0000-000000000003"), 22 },
                    { new Guid("44444444-4444-4444-4444-444444444441"), 3600m, "Soporte prioritario 24/7 con tiempo de respuesta garantizado de 1 hora", true, true, "SUPPORT_PREMIUM", null, null, null, "Soporte Premium 24/7", new Guid("10000000-0000-0000-0000-000000000004"), 30 },
                    { new Guid("44444444-4444-4444-4444-444444444442"), 6000m, "Account manager dedicado y soporte personalizado", true, true, "SUPPORT_DEDICATED", null, null, null, "Soporte Dedicado", new Guid("10000000-0000-0000-0000-000000000004"), 31 },
                    { new Guid("55555555-5555-5555-5555-555555555551"), 600m, "Límites extendidos de API (100K requests/día) y webhooks ilimitados", true, true, "FEATURE_API_EXTENDED", null, null, null, "API Extendida", new Guid("10000000-0000-0000-0000-000000000005"), 40 },
                    { new Guid("55555555-5555-5555-5555-555555555552"), 2400m, "Personalización completa con tu marca (logo, colores, dominio)", true, true, "FEATURE_WHITE_LABEL", null, null, null, "White Label", new Guid("10000000-0000-0000-0000-000000000005"), 41 },
                    { new Guid("55555555-5555-5555-5555-555555555553"), 1200m, "Autenticación única con SAML 2.0 y OAuth", true, true, "FEATURE_SSO", null, null, null, "Single Sign-On (SSO)", new Guid("10000000-0000-0000-0000-000000000005"), 42 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Modules_Name",
                table: "Modules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingPlanChanges_IsApplied",
                table: "PendingPlanChanges",
                column: "IsApplied");

            migrationBuilder.CreateIndex(
                name: "IX_PendingPlanChanges_SubscriptionId",
                table: "PendingPlanChanges",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanModules_ModuleId",
                table: "PlanModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanModules_PlanId",
                table: "PlanModules",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingHistories_PricingItemId",
                table: "PricingHistories",
                column: "PricingItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingItems_ItemCode",
                table: "PricingItems",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingItems_PricingItemTypeId",
                table: "PricingItems",
                column: "PricingItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingItemTypes_Code",
                table: "PricingItemTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalNotificationLogs_NotificationDate",
                table: "RenewalNotificationLogs",
                column: "NotificationDate");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalNotificationLogs_SubscriptionId",
                table: "RenewalNotificationLogs",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalNotificationLogs_TenantId",
                table: "RenewalNotificationLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModules_ModuleId",
                table: "TenantModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModules_SubscriptionId",
                table: "TenantModules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantModules_SubscriptionId_ModuleId",
                table: "TenantModules",
                columns: new[] { "SubscriptionId", "ModuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TransactionId",
                table: "TransactionDetails",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionHistories_TransactionId",
                table: "TransactionHistories",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SubscriptionId",
                table: "Transactions",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId",
                table: "Transactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionNumber",
                table: "Transactions",
                column: "TransactionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionStatusId",
                table: "Transactions",
                column: "TransactionStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionTypeId",
                table: "Transactions",
                column: "TransactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionStatuses_Code",
                table: "TransactionStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_Code",
                table: "TransactionTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingPlanChanges");

            migrationBuilder.DropTable(
                name: "PlanModules");

            migrationBuilder.DropTable(
                name: "PricingHistories");

            migrationBuilder.DropTable(
                name: "RenewalNotificationLogs");

            migrationBuilder.DropTable(
                name: "TenantModules");

            migrationBuilder.DropTable(
                name: "TransactionDetails");

            migrationBuilder.DropTable(
                name: "TransactionHistories");

            migrationBuilder.DropTable(
                name: "Modules");

            migrationBuilder.DropTable(
                name: "PricingItems");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "PricingItemTypes");

            migrationBuilder.DropTable(
                name: "TransactionStatuses");

            migrationBuilder.DropTable(
                name: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "ServiceLevel",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Plans");
        }
    }
}
