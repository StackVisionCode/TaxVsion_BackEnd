using System.Text.Json;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Application.Definitions.PublishCodeRule;
using TaxVision.Codes.Application.Definitions.ReactivateCode;
using TaxVision.Codes.Application.Definitions.RevokeCode;
using TaxVision.Codes.Application.Definitions.SuspendCode;
using TaxVision.Codes.Application.Definitions.UpdateCodeScopes;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

public sealed class CodesAdministrationApplicationTests
{
    /// <summary>P30: la fecha elegida vence al final de ese día en la zona comercial (Nueva York, LLC en Miami).</summary>
    [Fact]
    public async Task CreateCodeDefinition_expires_at_the_end_of_the_chosen_day_in_the_commercial_zone()
    {
        var definitions = new InMemoryCodeDefinitionRepository();

        var created = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(
                PlatformTenant.Id,
                CodeOwnerScope.Platform,
                tenantScopeId: null,
                "commercial-day"
            ) with
            {
                ExpiresAtUtc = null,
                ExpiresOn = new DateOnly(2026, 12, 31),
            },
            definitions,
            new SpyCodeTokenHasher(),
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.True(created.IsSuccess);
        // 23:59:59 del 31-dic en Nueva York (EST, UTC-5) = 04:59:59Z del 1-ene.
        Assert.Equal(
            new DateTime(2027, 1, 1, 4, 59, 59, DateTimeKind.Utc),
            Assert.Single(definitions.Definitions).ExpiresAtUtc
        );
        Assert.Equal("America/New_York", CommercialTimeZone.Id);
    }

    [Fact]
    public async Task CreateCodeDefinition_keeps_duration_usage_and_the_office_restriction()
    {
        var definitions = new InMemoryCodeDefinitionRepository();
        var office = Guid.Parse("30000000-0000-0000-0000-000000000001");

        var created = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(PlatformTenant.Id, CodeOwnerScope.Platform, tenantScopeId: null, "retention") with
            {
                Duration = CodeDurationKind.Months,
                DurationMonths = 3,
                UsableOn = CodeUsageContext.ExistingSubscription,
                RestrictToTenantId = office,
            },
            definitions,
            new SpyCodeTokenHasher(),
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
        var definition = Assert.Single(definitions.Definitions);
        Assert.Equal(CodeUsageContext.ExistingSubscription, definition.UsableOn);
        var rule = Assert.Single(definition.RuleVersions);
        Assert.Equal(CodeDurationKind.Months, rule.Duration.Kind);
        Assert.Equal(3, rule.Duration.Months);
        Assert.Contains(
            definition.Scopes,
            scope =>
                scope.Type == CodeScopeType.Tenant
                && scope.ScopeId == office.ToString()
                && scope.Mode == CodeScopeMode.Include
        );
    }

    [Fact]
    public async Task CreateCodeDefinition_rejects_months_without_a_valid_count()
    {
        var created = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(PlatformTenant.Id, CodeOwnerScope.Platform, tenantScopeId: null, "bad-months") with
            {
                Duration = CodeDurationKind.Months,
                DurationMonths = 48,
            },
            new InMemoryCodeDefinitionRepository(),
            new SpyCodeTokenHasher(),
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("Codes.CodeDuration.InvalidMonths", created.Error.Code);
    }

    [Fact]
    public async Task CreateCodeDefinition_enforces_platform_and_tenant_ownership()
    {
        var definitions = new InMemoryCodeDefinitionRepository();
        var hasher = new SpyCodeTokenHasher();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));

        var invalidPlatform = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(
                Guid.NewGuid(),
                CodeOwnerScope.Platform,
                tenantScopeId: null,
                "invalid-platform-owner"
            ),
            definitions,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        var invalidTenant = await CreateCodeDefinitionHandler.Handle(
            CreateFixedCommand(
                GrowthTestData.RefereeTenantId,
                CodeOwnerScope.Tenant,
                GrowthTestData.ReferrerTenantId,
                "invalid-tenant-owner"
            ),
            definitions,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        var platform = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(PlatformTenant.Id, CodeOwnerScope.Platform, tenantScopeId: null, "valid-platform"),
            definitions,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        var tenant = await CreateCodeDefinitionHandler.Handle(
            CreateFixedCommand(
                GrowthTestData.RefereeTenantId,
                CodeOwnerScope.Tenant,
                GrowthTestData.RefereeTenantId,
                "valid-tenant"
            ),
            definitions,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(invalidPlatform.IsFailure);
        Assert.Equal("Codes.CodeDefinition.InvalidPlatformOwner", invalidPlatform.Error.Code);
        Assert.True(invalidTenant.IsFailure);
        Assert.Equal("Codes.CodeDefinition.InvalidTenantScope", invalidTenant.Error.Code);
        Assert.True(platform.IsSuccess);
        Assert.Equal(PlatformTenant.Id, platform.Value.OwnerTenantId);
        Assert.True(tenant.IsSuccess);
        Assert.Equal(GrowthTestData.RefereeTenantId, tenant.Value.OwnerTenantId);
        Assert.Equal(2, definitions.Definitions.Count);
    }

    [Fact]
    public async Task Administrative_read_and_state_changes_are_owner_safe_and_secret_free()
    {
        const string token = "TENANT-ADMIN-1234";
        var definitions = new InMemoryCodeDefinitionRepository();
        var hasher = new SpyCodeTokenHasher();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var createCommand = CreateFixedCommand(
            GrowthTestData.RefereeTenantId,
            CodeOwnerScope.Tenant,
            GrowthTestData.RefereeTenantId,
            "tenant-admin-lifecycle",
            token
        );
        var created = await CreateCodeDefinitionHandler.Handle(
            createCommand,
            definitions,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(created.IsSuccess);

        var crossTenantRead = await GetCodeDetailsHandler.Handle(
            new GetCodeDetailsQuery(
                GrowthTestData.ReferrerTenantId,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId
            ),
            definitions,
            CancellationToken.None
        );
        var crossTenantActivate = await ActivateCodeHandler.Handle(
            new ActivateCodeCommand(
                GrowthTestData.ReferrerTenantId,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId,
                "cross-tenant-activate"
            ),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );
        var ownerRead = await GetCodeDetailsHandler.Handle(
            new GetCodeDetailsQuery(
                GrowthTestData.RefereeTenantId,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId
            ),
            definitions,
            CancellationToken.None
        );
        var activated = await ActivateCodeHandler.Handle(
            new ActivateCodeCommand(
                GrowthTestData.RefereeTenantId,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId,
                "owner-activate"
            ),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );
        var revoked = await RevokeCodeHandler.Handle(
            new RevokeCodeCommand(
                GrowthTestData.RefereeTenantId,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId,
                "owner-revoke"
            ),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(crossTenantRead.IsFailure);
        Assert.Equal("Codes.GetCodeDetails.NotFound", crossTenantRead.Error.Code);
        Assert.True(crossTenantActivate.IsFailure);
        Assert.Equal("Codes.ActivateCode.NotFound", crossTenantActivate.Error.Code);
        Assert.True(ownerRead.IsSuccess);
        Assert.True(activated.IsSuccess);
        Assert.Equal(CodeDefinitionStatus.Active.ToString(), activated.Value.Status);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(CodeDefinitionStatus.Revoked.ToString(), revoked.Value.Status);
        Assert.Contains("<redacted>", createCommand.ToString(), StringComparison.Ordinal);

        var serializedResponses = JsonSerializer.Serialize(
            new
            {
                Created = created.Value,
                OwnerRead = ownerRead.Value,
                Activated = activated.Value,
                Revoked = revoked.Value,
            }
        );
        Assert.DoesNotContain(token, serializedResponses, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SpyCodeTokenHasher.HashWithoutObservation(token).Value,
            serializedResponses,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Suspend_and_reactivate_change_the_status_of_an_owned_code()
    {
        var (definitions, codeId) = await CreatedAndActivatedAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var idempotency = new FakeBusinessIdempotencyExecutor();

        var foreign = await SuspendCodeHandler.Handle(
            new SuspendCodeCommand(GrowthTestData.RefereeTenantId, codeId, GrowthTestData.ActorId, "suspend-foreign"),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );
        var suspended = await SuspendCodeHandler.Handle(
            new SuspendCodeCommand(PlatformTenant.Id, codeId, GrowthTestData.ActorId, "suspend"),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );
        var reactivated = await ReactivateCodeHandler.Handle(
            new ReactivateCodeCommand(PlatformTenant.Id, codeId, GrowthTestData.ActorId, "reactivate"),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.Equal("Codes.SuspendCode.NotFound", foreign.Error.Code);
        Assert.Equal(CodeDefinitionStatus.Suspended.ToString(), suspended.Value.Status);
        Assert.Equal(CodeDefinitionStatus.Active.ToString(), reactivated.Value.Status);
    }

    [Fact]
    public async Task Reactivating_an_active_code_is_an_invalid_transition()
    {
        var (definitions, codeId) = await CreatedAndActivatedAsync();

        var reactivated = await ReactivateCodeHandler.Handle(
            new ReactivateCodeCommand(PlatformTenant.Id, codeId, GrowthTestData.ActorId, "reactivate-active"),
            definitions,
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("Codes.CodeDefinition.InvalidTransition", reactivated.Error.Code);
    }

    [Fact]
    public async Task Publishing_a_rule_adds_the_next_version_with_its_duration_and_stacking()
    {
        var (definitions, codeId) = await CreatedAndActivatedAsync();

        var published = await PublishCodeRuleHandler.Handle(
            new PublishCodeRuleCommand(
                PlatformTenant.Id,
                codeId,
                CodeBenefitType.Percentage,
                2_500,
                FixedAmountCents: null,
                FixedAmountCurrency: null,
                MinimumPurchaseAmountCents: null,
                MinimumPurchaseCurrency: null,
                AllowStacking: true,
                CodeDurationKind.Months,
                DurationMonths: 3,
                GrowthTestData.ActorId,
                "publish-rule"
            ),
            definitions,
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.True(published.IsSuccess, published.IsFailure ? published.Error.Code : null);
        var rule = published.Value.Rules.Last();
        Assert.Equal(2, rule.Version);
        Assert.Equal(2_500, rule.PercentageBasisPoints);
        Assert.True(rule.AllowStacking);
        var latest = Assert.Single(definitions.Definitions).RuleVersions.MaxBy(item => item.Version)!;
        Assert.Equal(CodeDurationKind.Months, latest.Duration.Kind);
        Assert.Equal(3, latest.Duration.Months);
    }

    [Fact]
    public async Task Publishing_a_rule_without_a_valid_benefit_is_rejected()
    {
        var (definitions, codeId) = await CreatedAndActivatedAsync();

        var published = await PublishCodeRuleHandler.Handle(
            new PublishCodeRuleCommand(
                PlatformTenant.Id,
                codeId,
                CodeBenefitType.Percentage,
                PercentageBasisPoints: null,
                FixedAmountCents: 500,
                FixedAmountCurrency: "USD",
                MinimumPurchaseAmountCents: null,
                MinimumPurchaseCurrency: null,
                AllowStacking: false,
                CodeDurationKind.Once,
                DurationMonths: null,
                GrowthTestData.ActorId,
                "publish-bad"
            ),
            definitions,
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("Codes.PublishCodeRule.InvalidPercentageBenefit", published.Error.Code);
        Assert.Single(Assert.Single(definitions.Definitions).RuleVersions);
    }

    [Fact]
    public async Task Updating_scopes_replaces_the_list_of_an_owned_code()
    {
        var (definitions, codeId) = await CreatedAndActivatedAsync();

        var updated = await UpdateCodeScopesHandler.Handle(
            new UpdateCodeScopesCommand(
                PlatformTenant.Id,
                codeId,
                [new CreateCodeScopeInput(CodeScopeType.Offer, "Subscription/pro/Annual", CodeScopeMode.Include)],
                GrowthTestData.ActorId,
                "update-scopes"
            ),
            definitions,
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.Code : null);
        var scope = Assert.Single(updated.Value.Scopes);
        Assert.Equal("Subscription/pro/Annual", scope.TargetId);
    }

    private static async Task<(InMemoryCodeDefinitionRepository Definitions, Guid CodeId)> CreatedAndActivatedAsync()
    {
        var definitions = new InMemoryCodeDefinitionRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var created = await CreateCodeDefinitionHandler.Handle(
            CreatePercentageCommand(PlatformTenant.Id, CodeOwnerScope.Platform, tenantScopeId: null, "lifecycle"),
            definitions,
            new SpyCodeTokenHasher(),
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(created.IsSuccess);
        var activated = await ActivateCodeHandler.Handle(
            new ActivateCodeCommand(
                PlatformTenant.Id,
                created.Value.CodeDefinitionId,
                GrowthTestData.ActorId,
                "lifecycle-on"
            ),
            definitions,
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(activated.IsSuccess);
        return (definitions, created.Value.CodeDefinitionId);
    }

    private static CreateCodeDefinitionCommand CreatePercentageCommand(
        Guid ownerTenantId,
        CodeOwnerScope ownerScope,
        Guid? tenantScopeId,
        string idempotencyKey,
        string token = "PLATFORM-PERCENT-1234"
    ) =>
        new(
            ownerTenantId,
            ownerScope,
            tenantScopeId,
            "Percentage administration code",
            CodeKind.Promotional,
            token,
            CodeBenefitType.Percentage,
            1_000,
            FixedAmountCents: null,
            FixedAmountCurrency: null,
            MinimumPurchaseAmountCents: null,
            MinimumPurchaseCurrency: null,
            AllowStacking: false,
            GrowthTestData.NowUtc.AddDays(-1),
            GrowthTestData.NowUtc.AddDays(30),
            100,
            10,
            2,
            [new CreateCodeScopeInput(CodeScopeType.Plan, "pro", CodeScopeMode.Include)],
            GrowthTestData.ActorId,
            idempotencyKey
        );

    private static CreateCodeDefinitionCommand CreateFixedCommand(
        Guid ownerTenantId,
        CodeOwnerScope ownerScope,
        Guid? tenantScopeId,
        string idempotencyKey,
        string token = "TENANT-FIXED-1234"
    ) =>
        new(
            ownerTenantId,
            ownerScope,
            tenantScopeId,
            "Fixed administration code",
            CodeKind.Promotional,
            token,
            CodeBenefitType.FixedAmount,
            PercentageBasisPoints: null,
            500,
            "USD",
            MinimumPurchaseAmountCents: null,
            MinimumPurchaseCurrency: null,
            AllowStacking: false,
            GrowthTestData.NowUtc.AddDays(-1),
            GrowthTestData.NowUtc.AddDays(30),
            100,
            10,
            2,
            [new CreateCodeScopeInput(CodeScopeType.Offer, "starter", CodeScopeMode.Include)],
            GrowthTestData.ActorId,
            idempotencyKey
        );
}
