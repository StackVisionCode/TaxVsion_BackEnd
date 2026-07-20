using BuildingBlocks.Tenancy;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Growth.Tests.Domain;

internal static class GrowthTestData
{
    internal static readonly DateTime NowUtc = new(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid ReferrerTenantId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    internal static readonly Guid RefereeTenantId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    internal static string Sha(char value = 'a') => new(value, 64);

    internal static CodeDefinition CreateActivePercentageCode(
        Guid? ownerTenantId = null,
        CodeOwnerScope ownerScope = CodeOwnerScope.Platform,
        Guid? tenantScopeId = null,
        int basisPoints = 1_000,
        long? maxRedemptions = 100,
        long? maxRedemptionsPerTenant = 20,
        long? maxRedemptionsPerSubject = 2,
        char codeHashCharacter = 'b'
    )
    {
        var owner = ownerTenantId ?? PlatformTenant.Id;
        var definition = CodeDefinition
            .Create(
                owner,
                ownerScope,
                tenantScopeId,
                "MVP 10 percent",
                CodeKind.Promotional,
                CodeTokenHash.Create(Sha(codeHashCharacter)).Value,
                CodeDisplay.Create("TV", "2026").Value,
                NowUtc.AddDays(-1),
                NowUtc.AddDays(30),
                maxRedemptions,
                maxRedemptionsPerTenant,
                maxRedemptionsPerSubject,
                ActorId,
                NowUtc
            )
            .Value;

        var published = definition.PublishRuleVersion(
            CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(basisPoints).Value).Value,
            minimumPurchase: null,
            allowStacking: false,
            ActorId,
            NowUtc
        );
        Assert.True(published.IsSuccess);
        Assert.True(definition.Activate(ActorId, NowUtc).IsSuccess);
        return definition;
    }

    internal static CodeQuote CreateQuote(
        CodeDefinition definition,
        Guid consumingTenantId,
        long grossAmountCents = 10_005,
        string subjectId = "subscription-1"
    ) =>
        definition
            .CreateQuote(
                consumingTenantId,
                SubjectReference.Create(SubjectType.Tenant, subjectId).Value,
                OfferReference.Create("Subscription", "pro", "v3").Value,
                [],
                Money.Create(grossAmountCents, "USD").Value,
                SnapshotHash.Create(Sha('c')).Value,
                IdempotencyKey.Create($"quote-{subjectId}").Value,
                PayloadFingerprint.Create(Sha('d')).Value,
                TimeSpan.FromMinutes(10),
                NowUtc
            )
            .Value;

    internal static CodeReservation CreateReservation(
        CodeDefinition definition,
        CodeQuote quote,
        Guid? paymentId = null
    )
    {
        Assert.True(definition.ReserveUse(NowUtc).IsSuccess);
        return CodeReservation
            .Create(
                quote,
                PaymentReference.Create("PaymentApp", paymentId ?? Guid.NewGuid()).Value,
                IdempotencyKey.Create("reserve-1").Value,
                PayloadFingerprint.Create(Sha('e')).Value,
                NowUtc.AddMinutes(5),
                NowUtc
            )
            .Value;
    }

    internal static ReferralProgram CreateActiveTenantReferralProgram()
    {
        var program = ReferralProgram
            .Create(
                "T2T-DEFAULT",
                "Tenant referral program",
                ReferralProgramScope.Platform,
                tenantScopeId: null,
                ReferralFlowType.TenantToTenant,
                ReferralProgramPolicy.TenantToTenantDefaults(),
                NowUtc.AddDays(-1),
                NowUtc.AddYears(1),
                "program-1",
                Sha('1'),
                ActorId,
                NowUtc
            )
            .Value;
        Assert.True(program.Activate(ActorId, NowUtc).IsSuccess);
        return program;
    }

    internal static ReferralCode CreateReferralCode(
        ReferralProgram program,
        Guid? ownerTenantId = null
    ) =>
        ReferralCode
            .Create(
                program,
                ReferralParticipantType.Tenant,
                ownerTenantId ?? ReferrerTenantId,
                Sha('2'),
                "REF",
                "2026",
                NowUtc.AddMonths(3),
                "referral-code-1",
                Sha('3'),
                ActorId,
                NowUtc
            )
            .Value;

    internal static ReferralAttribution CreateActiveAttribution(
        ReferralProgram program,
        ReferralCode referralCode,
        Guid? refereeTenantId = null
    )
    {
        var attribution = ReferralAttribution
            .Create(
                program,
                referralCode,
                ReferralParticipantType.Tenant,
                refereeTenantId ?? RefereeTenantId,
                "attribution-1",
                Sha('4'),
                ActorId,
                NowUtc
            )
            .Value;
        Assert.True(attribution.Activate(ActorId, NowUtc).IsSuccess);
        return attribution;
    }

    internal static ReferralQualification CreateQualifiedReferral(
        ReferralProgram program,
        ReferralAttribution attribution
    ) =>
        ReferralQualification
            .Evaluate(
                program,
                attribution,
                Guid.NewGuid(),
                Guid.NewGuid(),
                QualifyingPaymentSource.PaymentApp,
                25_00,
                "USD",
                isFirstSuccessfulPayment: true,
                annualRewardSlotAvailable: true,
                NowUtc.AddDays(1),
                "qualification-1",
                Sha('5'),
                ActorId,
                NowUtc.AddDays(1)
            )
            .Value;
}
