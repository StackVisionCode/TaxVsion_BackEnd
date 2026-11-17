using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Domain;

/// <summary>
/// Un código define cuánto dura su descuento (solo el primer cobro, N meses o para siempre) y dónde se usa (al
/// registrarse, en una suscripción existente o ambos). Los códigos de antes quedan como estaban: primer cobro, al
/// registrarse. Un código restringido a una oficina o a un comprador no sirve para otro.
/// </summary>
public sealed class CodeDurationTests
{
    private static readonly Guid Office = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOffice = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public void Existing_codes_default_to_once_on_signup()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var rule = Assert.Single(definition.RuleVersions);

        Assert.Equal(CodeDurationKind.Once, rule.Duration.Kind);
        Assert.Null(rule.Duration.Months);
        Assert.Equal(CodeUsageContext.Signup, definition.UsableOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void Months_requires_between_1_and_36(int months)
    {
        Assert.Equal("Codes.CodeDuration.InvalidMonths", CodeDuration.ForMonths(months).Error.Code);
        Assert.True(CodeDuration.ForMonths(1).IsSuccess);
        Assert.True(CodeDuration.ForMonths(36).IsSuccess);
    }

    [Fact]
    public void Gift_codes_only_allow_once()
    {
        var gift = Draft(CodeKind.BenefitGift);

        var monthly = gift.PublishRuleVersion(
            Percent(2_000),
            null,
            false,
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc,
            CodeDuration.ForMonths(3).Value
        );

        Assert.Equal("Codes.CodeDuration.GiftOnlyOnce", monthly.Error.Code);
    }

    [Fact]
    public void Signup_only_code_is_rejected_for_an_existing_subscription()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();

        var quote = Quote(definition, Office, [], CodeUsageContext.ExistingSubscription);

        Assert.Equal("Codes.CodeDefinition.NotUsableHere", quote.Error.Code);
    }

    [Fact]
    public void Office_own_code_cannot_discount_its_platform_subscription()
    {
        // Un código que la oficina creó para sus clientes no descuenta lo que la oficina le paga a la plataforma.
        var definition = CodeDefinition
            .Create(
                Office,
                CodeOwnerScope.Tenant,
                Office,
                "Office promo",
                CodeKind.Promotional,
                CodeTokenHash.Create(GrowthTestData.Sha('e')).Value,
                CodeDisplay.Create("TV", "2026").Value,
                GrowthTestData.NowUtc.AddDays(-1),
                GrowthTestData.NowUtc.AddDays(30),
                100,
                null,
                null,
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc,
                CodeUsageContext.ExistingSubscription
            )
            .Value;
        Assert.True(
            definition
                .PublishRuleVersion(Percent(10_000), null, false, GrowthTestData.ActorId, GrowthTestData.NowUtc)
                .IsSuccess
        );
        Assert.True(definition.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        var quote = Quote(definition, Office, [Target(CodeScopeType.Tenant, Office)]);

        Assert.Equal("Codes.CodeDefinition.NotUsableHere", quote.Error.Code);
    }

    [Fact]
    public void Code_for_existing_subscriptions_is_rejected_at_signup()
    {
        var definition = Active(CodeUsageContext.ExistingSubscription);

        Assert.Equal(
            "Codes.CodeDefinition.NotUsableHere",
            Quote(definition, PlatformTenant.Id, [], CodeUsageContext.Signup).Error.Code
        );
        Assert.True(Quote(definition, Office, [], CodeUsageContext.ExistingSubscription).IsSuccess);
    }

    [Fact]
    public void Code_restricted_to_an_office_is_rejected_for_another_office()
    {
        var definition = Active(CodeUsageContext.ExistingSubscription, (CodeScopeType.Tenant, Office.ToString()));

        Assert.True(Quote(definition, Office, [Target(CodeScopeType.Tenant, Office)]).IsSuccess);
        Assert.Equal(
            "Codes.CodeDefinition.ScopeNotIncluded",
            Quote(definition, OtherOffice, [Target(CodeScopeType.Tenant, OtherOffice)]).Error.Code
        );
    }

    [Fact]
    public void Code_restricted_to_an_email_is_rejected_for_another_email()
    {
        const string ana = "v1:0000000000000000000000000000000a";
        const string luis = "v1:0000000000000000000000000000000b";
        var definition = Active(CodeUsageContext.Signup, (CodeScopeType.Subject, ana));

        Assert.True(
            Quote(
                definition,
                PlatformTenant.Id,
                [CodeScopeTarget.Create(CodeScopeType.Subject, ana).Value],
                CodeUsageContext.Signup
            ).IsSuccess
        );
        Assert.Equal(
            "Codes.CodeDefinition.ScopeNotIncluded",
            Quote(
                definition,
                PlatformTenant.Id,
                [CodeScopeTarget.Create(CodeScopeType.Subject, luis).Value],
                CodeUsageContext.Signup
            ).Error.Code
        );
    }

    [Fact]
    public void Restriction_to_an_office_holds_even_when_the_plan_also_matches()
    {
        // Los alcances de distinto tipo se suman: oficina Y plan, no oficina O plan.
        var definition = Active(
            CodeUsageContext.ExistingSubscription,
            (CodeScopeType.Tenant, Office.ToString()),
            (CodeScopeType.Plan, "pro")
        );

        var otherOffice = Quote(
            definition,
            OtherOffice,
            [Target(CodeScopeType.Tenant, OtherOffice), CodeScopeTarget.Create(CodeScopeType.Plan, "pro").Value]
        );

        Assert.Equal("Codes.CodeDefinition.ScopeNotIncluded", otherOffice.Error.Code);
    }

    private static CodeDefinition Draft(CodeKind kind, CodeUsageContext usableOn = CodeUsageContext.Signup) =>
        CodeDefinition
            .Create(
                PlatformTenant.Id,
                CodeOwnerScope.Platform,
                null,
                "Duration test",
                kind,
                CodeTokenHash.Create(GrowthTestData.Sha('e')).Value,
                CodeDisplay.Create("TV", "2026").Value,
                GrowthTestData.NowUtc.AddDays(-1),
                GrowthTestData.NowUtc.AddDays(30),
                100,
                null,
                null,
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc,
                usableOn
            )
            .Value;

    private static CodeDefinition Active(CodeUsageContext usableOn, params (CodeScopeType Type, string Id)[] includes)
    {
        var definition = Draft(CodeKind.Promotional, usableOn);
        Assert.True(
            definition
                .PublishRuleVersion(Percent(2_000), null, false, GrowthTestData.ActorId, GrowthTestData.NowUtc)
                .IsSuccess
        );
        foreach (var (type, id) in includes)
            Assert.True(
                definition
                    .AddScope(type, id, CodeScopeMode.Include, GrowthTestData.ActorId, GrowthTestData.NowUtc)
                    .IsSuccess
            );
        Assert.True(definition.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        return definition;
    }

    private static CodeBenefit Percent(int basisPoints) =>
        CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(basisPoints).Value).Value;

    private static CodeScopeTarget Target(CodeScopeType type, Guid id) =>
        CodeScopeTarget.Create(type, id.ToString()).Value;

    private static Result<CodeQuote> Quote(
        CodeDefinition definition,
        Guid consumingTenantId,
        IReadOnlyCollection<CodeScopeTarget> targets,
        CodeUsageContext context = CodeUsageContext.ExistingSubscription
    ) =>
        definition.CreateQuote(
            consumingTenantId,
            SubjectReference.Create(SubjectType.Tenant, "subscription-1").Value,
            OfferReference.Create("Subscription", "pro", "v3").Value,
            targets,
            Money.Create(10_000, "USD").Value,
            SnapshotHash.Create(GrowthTestData.Sha('c')).Value,
            IdempotencyKey.Create($"quote-{Guid.NewGuid():N}").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('d')).Value,
            TimeSpan.FromMinutes(10),
            GrowthTestData.NowUtc,
            context
        );
}
