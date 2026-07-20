using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGrowth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "codes");

            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.EnsureSchema(
                name: "referrals");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoundedContext = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CausationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodeDefinitions",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CodeHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CodePrefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CodeLastFour = table.Column<string>(type: "char(4)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    MaxRedemptions = table.Column<long>(type: "bigint", nullable: true),
                    MaxRedemptionsPerTenant = table.Column<long>(type: "bigint", nullable: true),
                    MaxRedemptionsPerSubject = table.Column<long>(type: "bigint", nullable: true),
                    ActiveReservations = table.Column<long>(type: "bigint", nullable: false),
                    CommittedRedemptions = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_CodeDefinitions_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.CheckConstraint("CK_CodeDefinitions_Counters", "[ActiveReservations] >= 0 AND [CommittedRedemptions] >= 0");
                    table.CheckConstraint("CK_CodeDefinitions_Period", "[ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > [StartsAtUtc]");
                });

            migrationBuilder.CreateTable(
                name: "ProcessedBusinessMessages",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedBusinessMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferralPrograms",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FlowType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttributionWindowDays = table.Column<int>(type: "int", nullable: false),
                    QualifyingPaymentSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QualifyingEventRule = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MinimumPaymentAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    MinimumPaymentCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    WaitingPeriodDays = table.Column<int>(type: "int", nullable: false),
                    MaximumRewardsPerReferrerPerCalendarYear = table.Column<int>(type: "int", nullable: false),
                    RewardType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RewardDefinitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralPrograms", x => x.Id);
                    table.CheckConstraint("CK_ReferralPrograms_FlowScope", "([FlowType] = N'TenantToTenant' AND [ScopeType] = N'Platform') OR ([FlowType] = N'TaxpayerToTaxpayer' AND [ScopeType] = N'Tenant')");
                    table.CheckConstraint("CK_ReferralPrograms_Period", "[EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
                    table.CheckConstraint("CK_ReferralPrograms_PolicyLimits", "[AttributionWindowDays] > 0 AND [MinimumPaymentAmountCents] >= 0 AND [WaitingPeriodDays] >= 0 AND [MaximumRewardsPerReferrerPerCalendarYear] > 0");
                    table.CheckConstraint("CK_ReferralPrograms_TenantScope", "([ScopeType] = N'Platform' AND [TenantScopeId] IS NULL) OR ([ScopeType] = N'Tenant' AND [TenantScopeId] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "ReferralRewardQuotaCounters",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarYear = table.Column<int>(type: "int", nullable: false),
                    Maximum = table.Column<int>(type: "int", nullable: false),
                    ReservedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRewardQuotaCounters", x => x.Id);
                    table.CheckConstraint("CK_ReferralRewardQuotaCounters_Count", "[Maximum] > 0 AND [ReservedCount] >= 0 AND [ReservedCount] <= [Maximum]");
                    table.CheckConstraint("CK_ReferralRewardQuotaCounters_Year", "[CalendarYear] BETWEEN 2000 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "ReferralRewardQuotaReservations",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarYear = table.Column<int>(type: "int", nullable: false),
                    QualificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRewardQuotaReservations", x => x.Id);
                    table.CheckConstraint("CK_ReferralRewardQuotaReservations_Year", "[CalendarYear] BETWEEN 2000 AND 9999");
                });

            migrationBuilder.CreateTable(
                name: "CodeRules",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Benefit_Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BenefitBasisPoints = table.Column<int>(type: "int", nullable: true),
                    BenefitFixedAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    BenefitFixedCurrency = table.Column<string>(type: "char(3)", nullable: true),
                    Benefit_GrantKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Benefit_DurationDays = table.Column<int>(type: "int", nullable: true),
                    MinimumPurchaseAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    MinimumPurchaseCurrency = table.Column<string>(type: "char(3)", nullable: true),
                    AllowStacking = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRules_CodeDefinitions_CodeDefinitionId_TenantId",
                        columns: x => new { x.CodeDefinitionId, x.TenantId },
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeScopes",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeScopes_CodeDefinitions_CodeDefinitionId_TenantId",
                        columns: x => new { x.CodeDefinitionId, x.TenantId },
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeUsageCounters",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    MaxRedemptions = table.Column<long>(type: "bigint", nullable: false),
                    ActiveReservations = table.Column<long>(type: "bigint", nullable: false),
                    CommittedRedemptions = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeUsageCounters", x => x.Id);
                    table.CheckConstraint("CK_CodeUsageCounters_Counts", "[ActiveReservations] >= 0 AND [CommittedRedemptions] >= 0 AND [ActiveReservations] + [CommittedRedemptions] <= [MaxRedemptions]");
                    table.CheckConstraint("CK_CodeUsageCounters_Limit", "[MaxRedemptions] > 0");
                    table.ForeignKey(
                        name: "FK_CodeUsageCounters_CodeDefinitions_CodeDefinitionId",
                        column: x => x.CodeDefinitionId,
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCodes",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    DisplayPrefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    LastFour = table.Column<string>(type: "char(4)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralCodes_ReferralPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "referrals",
                        principalTable: "ReferralPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodeQuotes",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeRuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    CodePrefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CodeLastFour = table.Column<string>(type: "char(4)", nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OfferOwner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OfferId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OfferVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrossAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    GrossCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    DiscountAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    DiscountCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    NetAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    NetCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeQuotes", x => x.Id);
                    table.CheckConstraint("CK_CodeQuotes_Amounts", "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 AND [NetAmountCents] >= 0 AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]");
                    table.ForeignKey(
                        name: "FK_CodeQuotes_CodeDefinitions_CodeDefinitionId",
                        column: x => x.CodeDefinitionId,
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CodeQuotes_CodeRules_CodeRuleVersionId",
                        column: x => x.CodeRuleVersionId,
                        principalSchema: "codes",
                        principalTable: "CodeRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralAttributions",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferrerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefereeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefereeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusBeforeReview = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AttributedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    QualifiedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralAttributions", x => x.Id);
                    table.CheckConstraint("CK_ReferralAttributions_NoSelfReferral", "[ReferrerType] <> [RefereeType] OR [ReferrerId] <> [RefereeId]");
                    table.ForeignKey(
                        name: "FK_ReferralAttributions_ReferralCodes_ReferralCodeId",
                        column: x => x.ReferralCodeId,
                        principalSchema: "referrals",
                        principalTable: "ReferralCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralAttributions_ReferralPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "referrals",
                        principalTable: "ReferralPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodeReservations",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RelatedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GrossAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    GrossCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    DiscountAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    DiscountCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    NetAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    NetCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReservationIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReservationPayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CommitIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CommitPayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    CancellationIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancellationPayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RedemptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastCompensationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommitSourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasLateCommit = table.Column<bool>(type: "bit", nullable: false),
                    IsAvailabilityReleased = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CompensatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeReservations", x => x.Id);
                    table.CheckConstraint("CK_CodeReservations_Amounts", "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 AND [NetAmountCents] >= 0 AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]");
                    table.ForeignKey(
                        name: "FK_CodeReservations_CodeDefinitions_CodeDefinitionId",
                        column: x => x.CodeDefinitionId,
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CodeReservations_CodeQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalSchema: "codes",
                        principalTable: "CodeQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralQualifications",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualifyingEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PaymentAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    PaymentCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    IsFirstSuccessfulPayment = table.Column<bool>(type: "bit", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RejectionReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentSucceededAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RewardEligibleAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    EvaluatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralQualifications", x => x.Id);
                    table.CheckConstraint("CK_ReferralQualifications_PaymentAmount", "[PaymentAmountCents] > 0");
                    table.ForeignKey(
                        name: "FK_ReferralQualifications_ReferralAttributions_AttributionId",
                        column: x => x.AttributionId,
                        principalSchema: "referrals",
                        principalTable: "ReferralAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralQualifications_ReferralPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "referrals",
                        principalTable: "ReferralPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodeRedemptions",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RelatedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    GrossCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    DiscountAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    DiscountCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    NetAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    NetCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CommitIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CommitPayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WasLateCommit = table.Column<bool>(type: "bit", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRedemptions", x => x.Id);
                    table.CheckConstraint("CK_CodeRedemptions_Amounts", "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 AND [NetAmountCents] >= 0 AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]");
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_CodeDefinitions_CodeDefinitionId",
                        column: x => x.CodeDefinitionId,
                        principalSchema: "codes",
                        principalTable: "CodeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_CodeReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "codes",
                        principalTable: "CodeReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralRewardCases",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BeneficiaryType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RewardDefinitionKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EligibleAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    MaterializedBenefitReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StateReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRewardCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralRewardCases_ReferralAttributions_AttributionId",
                        column: x => x.AttributionId,
                        principalSchema: "referrals",
                        principalTable: "ReferralAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralRewardCases_ReferralPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "referrals",
                        principalTable: "ReferralPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralRewardCases_ReferralQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalSchema: "referrals",
                        principalTable: "ReferralQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodeCompensations",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RedemptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AdjustmentAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    CumulativeAdjustmentAmountCents = table.Column<long>(type: "bigint", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeCompensations", x => x.Id);
                    table.CheckConstraint("CK_CodeCompensations_Adjustment", "[AdjustmentAmountCents] >= 0 AND [CumulativeAdjustmentAmountCents] >= 0");
                    table.ForeignKey(
                        name: "FK_CodeCompensations_CodeRedemptions_RedemptionId",
                        column: x => x.RedemptionId,
                        principalSchema: "codes",
                        principalTable: "CodeRedemptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralFraudReviews",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RewardCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SignalCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolutionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralFraudReviews", x => x.Id);
                    table.CheckConstraint("CK_ReferralFraudReviews_Target", "[AttributionId] IS NOT NULL OR [RewardCaseId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ReferralFraudReviews_ReferralAttributions_AttributionId",
                        column: x => x.AttributionId,
                        principalSchema: "referrals",
                        principalTable: "ReferralAttributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralFraudReviews_ReferralPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "referrals",
                        principalTable: "ReferralPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralFraudReviews_ReferralRewardCases_RewardCaseId",
                        column: x => x.RewardCaseId,
                        principalSchema: "referrals",
                        principalTable: "ReferralRewardCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralRewardAttempts",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletionIdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompletionPayloadFingerprint = table.Column<string>(type: "char(64)", fixedLength: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRewardAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferralRewardAttempts_ReferralRewardCases_RewardCaseId",
                        column: x => x.RewardCaseId,
                        principalSchema: "referrals",
                        principalTable: "ReferralRewardCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Aggregate_OccurredAtUtc",
                schema: "audit",
                table: "AuditEntries",
                columns: new[] { "AggregateType", "AggregateId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_OccurredAtUtc",
                schema: "audit",
                table: "AuditEntries",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeCompensations_RedemptionId",
                schema: "codes",
                table: "CodeCompensations",
                column: "RedemptionId");

            migrationBuilder.CreateIndex(
                name: "UX_CodeCompensations_Tenant_Redemption_Event",
                schema: "codes",
                table: "CodeCompensations",
                columns: new[] { "TenantId", "RedemptionId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CodeCompensations_TenantId_IdempotencyKey",
                schema: "codes",
                table: "CodeCompensations",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeDefinitions_TenantId_Status",
                schema: "codes",
                table: "CodeDefinitions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_CodeDefinitions_TenantScopeId_CodeHash",
                schema: "codes",
                table: "CodeDefinitions",
                columns: new[] { "TenantScopeId", "CodeHash" },
                unique: true,
                filter: "[TenantScopeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CodeQuotes_CodeDefinitionId",
                schema: "codes",
                table: "CodeQuotes",
                column: "CodeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeQuotes_CodeRuleVersionId",
                schema: "codes",
                table: "CodeQuotes",
                column: "CodeRuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeQuotes_TenantId_ExpiresAtUtc",
                schema: "codes",
                table: "CodeQuotes",
                columns: new[] { "TenantId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CodeQuotes_TenantId_IdempotencyKey",
                schema: "codes",
                table: "CodeQuotes",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_CodeDefinitionId",
                schema: "codes",
                table: "CodeRedemptions",
                column: "CodeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CodeRedemptions_ReservationId",
                schema: "codes",
                table: "CodeRedemptions",
                column: "ReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeReservations_CodeDefinitionId",
                schema: "codes",
                table: "CodeReservations",
                column: "CodeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeReservations_QuoteId",
                schema: "codes",
                table: "CodeReservations",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeReservations_Status_ExpiresAtUtc",
                schema: "codes",
                table: "CodeReservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CodeReservations_Payment",
                schema: "codes",
                table: "CodeReservations",
                columns: new[] { "PaymentSource", "RelatedPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CodeReservations_TenantId_IdempotencyKey",
                schema: "codes",
                table: "CodeReservations",
                columns: new[] { "TenantId", "ReservationIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeRules_CodeDefinitionId_TenantId",
                schema: "codes",
                table: "CodeRules",
                columns: new[] { "CodeDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_CodeRules_CodeDefinitionId_Version",
                schema: "codes",
                table: "CodeRules",
                columns: new[] { "CodeDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeScopes_CodeDefinitionId_TenantId",
                schema: "codes",
                table: "CodeScopes",
                columns: new[] { "CodeDefinitionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_CodeScopes_CodeDefinition_Target_Mode",
                schema: "codes",
                table: "CodeScopes",
                columns: new[] { "CodeDefinitionId", "Type", "ScopeId", "Mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeUsageCounters_CodeDefinitionId",
                schema: "codes",
                table: "CodeUsageCounters",
                column: "CodeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CodeUsageCounters_Tenant_Code_Dimension_Scope",
                schema: "codes",
                table: "CodeUsageCounters",
                columns: new[] { "TenantId", "CodeDefinitionId", "Dimension", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedBusinessMessages_Status_ExpiresAtUtc",
                schema: "integration",
                table: "ProcessedBusinessMessages",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ProcessedBusinessMessages_Tenant_Operation_Scope_Key",
                schema: "integration",
                table: "ProcessedBusinessMessages",
                columns: new[] { "TenantId", "Operation", "ScopeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralAttributions_ReferralCodeId",
                schema: "referrals",
                table: "ReferralAttributions",
                column: "ReferralCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralAttributions_ActiveReferee",
                schema: "referrals",
                table: "ReferralAttributions",
                columns: new[] { "ProgramId", "RefereeType", "RefereeId" },
                unique: true,
                filter: "[Status] IN (N'Pending', N'Active', N'Qualified', N'UnderReview')");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralAttributions_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralAttributions",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralCodes_ActiveOwner",
                schema: "referrals",
                table: "ReferralCodes",
                columns: new[] { "ProgramId", "OwnerType", "OwnerId" },
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralCodes_ProgramId_CodeHash",
                schema: "referrals",
                table: "ReferralCodes",
                columns: new[] { "ProgramId", "CodeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralCodes_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralCodes",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralFraudReviews_AttributionId",
                schema: "referrals",
                table: "ReferralFraudReviews",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralFraudReviews_ProgramId",
                schema: "referrals",
                table: "ReferralFraudReviews",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralFraudReviews_RewardCaseId",
                schema: "referrals",
                table: "ReferralFraudReviews",
                column: "RewardCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralFraudReviews_Status_CreatedAtUtc",
                schema: "referrals",
                table: "ReferralFraudReviews",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralFraudReviews_TenantId_Status",
                schema: "referrals",
                table: "ReferralFraudReviews",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ReferralFraudReviews_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralFraudReviews",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralPrograms_Scope_ProgramCode",
                schema: "referrals",
                table: "ReferralPrograms",
                columns: new[] { "ScopeType", "TenantScopeId", "ProgramCode" },
                unique: true,
                filter: "[TenantScopeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralPrograms_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralPrograms",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralQualifications_ProgramId",
                schema: "referrals",
                table: "ReferralQualifications",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralQualifications_Attribution_Event",
                schema: "referrals",
                table: "ReferralQualifications",
                columns: new[] { "AttributionId", "QualifyingEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralQualifications_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralQualifications",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardAttempts_RewardCase_CompletionKey",
                schema: "referrals",
                table: "ReferralRewardAttempts",
                columns: new[] { "RewardCaseId", "CompletionIdempotencyKey" },
                unique: true,
                filter: "[CompletionIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardAttempts_RewardCase_IdempotencyKey",
                schema: "referrals",
                table: "ReferralRewardAttempts",
                columns: new[] { "RewardCaseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRewardCases_AttributionId",
                schema: "referrals",
                table: "ReferralRewardCases",
                column: "AttributionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRewardCases_ProgramId",
                schema: "referrals",
                table: "ReferralRewardCases",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardCases_GrantId",
                schema: "referrals",
                table: "ReferralRewardCases",
                column: "GrantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardCases_Qualification_Beneficiary_Reward",
                schema: "referrals",
                table: "ReferralRewardCases",
                columns: new[] { "QualificationId", "BeneficiaryType", "BeneficiaryId", "RewardType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardCases_TenantId_IdempotencyKey",
                schema: "referrals",
                table: "ReferralRewardCases",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardQuotaCounters_Owner_Program_Referrer_Year",
                schema: "referrals",
                table: "ReferralRewardQuotaCounters",
                columns: new[] { "TenantId", "ProgramId", "ReferrerId", "CalendarYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRewardQuotaReservations_Quota",
                schema: "referrals",
                table: "ReferralRewardQuotaReservations",
                columns: new[] { "TenantId", "ProgramId", "ReferrerId", "CalendarYear" });

            migrationBuilder.CreateIndex(
                name: "UX_ReferralRewardQuotaReservations_QualificationId",
                schema: "referrals",
                table: "ReferralRewardQuotaReservations",
                column: "QualificationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CodeCompensations",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "CodeScopes",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "CodeUsageCounters",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ProcessedBusinessMessages",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "ReferralFraudReviews",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "ReferralRewardAttempts",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "ReferralRewardQuotaCounters",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "ReferralRewardQuotaReservations",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "CodeRedemptions",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ReferralRewardCases",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "CodeReservations",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ReferralQualifications",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "CodeQuotes",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ReferralAttributions",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "CodeRules",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ReferralCodes",
                schema: "referrals");

            migrationBuilder.DropTable(
                name: "CodeDefinitions",
                schema: "codes");

            migrationBuilder.DropTable(
                name: "ReferralPrograms",
                schema: "referrals");
        }
    }
}
