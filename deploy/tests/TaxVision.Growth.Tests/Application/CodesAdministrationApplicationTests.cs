using System.Text.Json;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Definitions.ActivateCode;
using TaxVision.Codes.Application.Definitions.CreateCodeDefinition;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Application.Definitions.RevokeCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

public sealed class CodesAdministrationApplicationTests
{
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
            CreatePercentageCommand(
                PlatformTenant.Id,
                CodeOwnerScope.Platform,
                tenantScopeId: null,
                "valid-platform"
            ),
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
