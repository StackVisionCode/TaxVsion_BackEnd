using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Domain;

/// <summary>Administración de un código: suspender, nueva versión de regla y alcances.</summary>
public sealed class CodeDefinitionTests
{
    private static readonly DateTime Now = GrowthTestData.NowUtc;

    [Fact]
    public void Suspended_code_cannot_be_reserved()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();

        Assert.True(definition.Suspend(GrowthTestData.ActorId, Now).IsSuccess);

        Assert.Equal("Codes.CodeDefinition.NotReservable", definition.ReserveUse(Now).Error.Code);
        Assert.True(definition.Reactivate(GrowthTestData.ActorId, Now).IsSuccess);
        Assert.True(definition.ReserveUse(Now).IsSuccess);
    }

    [Fact]
    public void New_rule_version_does_not_change_existing_reservations()
    {
        var definition = GrowthTestData.CreateActivePercentageCode(basisPoints: 1_000);
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId, grossAmountCents: 10_000);
        var reservation = GrowthTestData.CreateReservation(definition, quote);

        var published = definition.PublishRuleVersion(
            CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(5_000).Value).Value,
            minimumPurchase: null,
            allowStacking: true,
            GrowthTestData.ActorId,
            Now
        );

        Assert.True(published.IsSuccess);
        Assert.Equal(2, published.Value.Version);
        Assert.Equal(1_000, reservation.DiscountAmount.AmountCents);
        Assert.Equal(1, quote.RuleVersion);
        var next = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId, 10_000, "subscription-2");
        Assert.Equal(5_000, next.DiscountAmount.AmountCents);
    }

    [Fact]
    public void Plan_scope_include_only_matches_that_plan()
    {
        var definition = GrowthTestData.CreateActiveCode(
            CodeUsageContext.Signup,
            includes: (CodeScopeType.Plan, "plan-pro")
        );

        Assert.True(Quote(definition, (CodeScopeType.Plan, "plan-pro")).IsSuccess);
        Assert.Equal(
            "Codes.CodeDefinition.ScopeNotIncluded",
            Quote(definition, (CodeScopeType.Plan, "plan-basic")).Error.Code
        );
    }

    [Fact]
    public void Replacing_scopes_swaps_the_whole_list()
    {
        var definition = GrowthTestData.CreateActiveCode(
            CodeUsageContext.Signup,
            includes: (CodeScopeType.Plan, "plan-pro")
        );

        var replaced = definition.ReplaceScopes(
            [
                (CodeScopeType.Plan, "plan-basic", CodeScopeMode.Include),
                (CodeScopeType.Offer, "Subscription/plan-basic/Monthly", CodeScopeMode.Exclude),
            ],
            GrowthTestData.ActorId,
            Now
        );

        Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error.Code : null);
        Assert.Equal(2, definition.Scopes.Count);
        Assert.DoesNotContain(definition.Scopes, scope => scope.ScopeId == "plan-pro");
        Assert.True(Quote(definition, (CodeScopeType.Plan, "plan-basic")).IsSuccess);
    }

    [Fact]
    public void An_invalid_scope_list_keeps_the_previous_scopes()
    {
        var definition = GrowthTestData.CreateActiveCode(
            CodeUsageContext.Signup,
            includes: (CodeScopeType.Plan, "plan-pro")
        );

        var replaced = definition.ReplaceScopes(
            [
                (CodeScopeType.Plan, "plan-basic", CodeScopeMode.Include),
                (CodeScopeType.Plan, "plan-basic", CodeScopeMode.Include),
            ],
            GrowthTestData.ActorId,
            Now
        );

        Assert.Equal("Codes.CodeDefinition.DuplicateScope", replaced.Error.Code);
        Assert.Equal("plan-pro", Assert.Single(definition.Scopes).ScopeId);
    }

    [Fact]
    public void A_revoked_code_cannot_change_its_scopes()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        Assert.True(definition.Revoke(GrowthTestData.ActorId, Now).IsSuccess);

        var replaced = definition.ReplaceScopes([], GrowthTestData.ActorId, Now);

        Assert.Equal("Codes.CodeDefinition.Terminal", replaced.Error.Code);
    }

    [Fact]
    public void Non_stackable_code_rejects_a_second_code()
    {
        var exclusive = GrowthTestData.CreateActivePercentageCode();
        var stackable = Stackable(GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'f'));

        Assert.Equal("Codes.CodeDefinition.NotStackable", exclusive.EnsureStackableWith([stackable]).Error.Code);
        Assert.Equal("Codes.CodeDefinition.NotStackable", stackable.EnsureStackableWith([exclusive]).Error.Code);
        Assert.True(exclusive.EnsureStackableWith([]).IsSuccess);
    }

    [Fact]
    public void Stackable_codes_combine()
    {
        var first = Stackable(GrowthTestData.CreateActivePercentageCode());
        var second = Stackable(GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'f'));

        Assert.True(second.EnsureStackableWith([first]).IsSuccess);
    }

    internal static CodeDefinition Stackable(CodeDefinition definition)
    {
        Assert.True(
            definition
                .PublishRuleVersion(
                    CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(1_000).Value).Value,
                    minimumPurchase: null,
                    allowStacking: true,
                    GrowthTestData.ActorId,
                    Now
                )
                .IsSuccess
        );
        return definition;
    }

    private static BuildingBlocks.Results.Result<CodeQuote> Quote(
        CodeDefinition definition,
        params (CodeScopeType Type, string Id)[] targets
    ) =>
        definition.CreateQuote(
            GrowthTestData.RefereeTenantId,
            SubjectReference.Create(SubjectType.Tenant, "subscription-1").Value,
            OfferReference.Create("Subscription", "plan", "1").Value,
            targets.Select(target => CodeScopeTarget.Create(target.Type, target.Id).Value).ToList(),
            Money.Create(10_000, "USD").Value,
            SnapshotHash.Create(GrowthTestData.Sha('c')).Value,
            IdempotencyKey.Create("quote-scope").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('d')).Value,
            TimeSpan.FromMinutes(10),
            Now
        );
}
