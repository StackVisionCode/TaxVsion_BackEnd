using BuildingBlocks.Tenancy;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.Usage;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Domain;

public sealed class CodesDomainTests
{
    [Fact]
    public void Platform_code_must_use_the_canonical_platform_tenant()
    {
        var result = CodeDefinition.Create(
            Guid.NewGuid(),
            CodeOwnerScope.Platform,
            tenantScopeId: null,
            "invalid platform owner",
            CodeKind.Promotional,
            CodeTokenHash.Create(GrowthTestData.Sha('6')).Value,
            CodeDisplay.Create("TV", "2026").Value,
            GrowthTestData.NowUtc,
            GrowthTestData.NowUtc.AddDays(1),
            10,
            5,
            1,
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Codes.CodeDefinition.InvalidPlatformOwner", result.Error.Code);
    }

    [Fact]
    public void Percentage_quote_freezes_rule_offer_and_amount_snapshot()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(
            definition,
            GrowthTestData.RefereeTenantId,
            grossAmountCents: 10_005
        );

        Assert.Equal(10_005, quote.GrossAmount.AmountCents);
        Assert.Equal(1_001, quote.DiscountAmount.AmountCents);
        Assert.Equal(9_004, quote.NetAmount.AmountCents);
        Assert.Equal("USD", quote.NetAmount.Currency);
        Assert.Equal("Subscription", quote.Offer.Owner);
        Assert.Equal("pro", quote.Offer.OfferId);
        Assert.Equal("v3", quote.Offer.OfferVersion);
        Assert.Equal(GrowthTestData.Sha('c'), quote.SnapshotHash.Value);
        Assert.Equal(1, quote.RuleVersion);
    }

    [Fact]
    public void Cancelled_reservation_cannot_be_committed_and_releases_availability_once()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId);
        var reservation = GrowthTestData.CreateReservation(definition, quote);

        var cancelled = reservation.Cancel(
            IdempotencyKey.Create("cancel-1").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('7')).Value,
            "payment failed",
            GrowthTestData.NowUtc.AddMinutes(1)
        );
        Assert.True(cancelled.IsSuccess);
        Assert.True(definition.ReleaseReservedUse(GrowthTestData.NowUtc.AddMinutes(1)).IsSuccess);
        Assert.True(reservation.MarkAvailabilityReleased(GrowthTestData.NowUtc.AddMinutes(1)).IsSuccess);
        Assert.True(reservation.MarkAvailabilityReleased(GrowthTestData.NowUtc.AddMinutes(2)).IsSuccess);

        var committed = reservation.Commit(
            IdempotencyKey.Create("commit-after-cancel").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('8')).Value,
            Guid.NewGuid(),
            allowLateCommit: true,
            GrowthTestData.NowUtc.AddMinutes(2)
        );

        Assert.True(committed.IsFailure);
        Assert.Equal(CodeReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(0, definition.ActiveReservations);
    }

    [Fact]
    public void Expired_released_reservation_supports_verified_late_commit_path()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(definition, GrowthTestData.RefereeTenantId);
        var reservation = GrowthTestData.CreateReservation(definition, quote);
        var expiredAt = GrowthTestData.NowUtc.AddMinutes(6);

        Assert.True(reservation.Expire(expiredAt).IsSuccess);
        Assert.True(definition.ReleaseReservedUse(expiredAt).IsSuccess);
        Assert.True(reservation.MarkAvailabilityReleased(expiredAt).IsSuccess);

        var redemption = reservation.Commit(
            IdempotencyKey.Create("late-commit").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('9')).Value,
            Guid.NewGuid(),
            allowLateCommit: true,
            expiredAt.AddMinutes(1)
        );
        Assert.True(redemption.IsSuccess);
        Assert.True(definition.CommitLateUse(expiredAt.AddMinutes(1)).IsSuccess);
        Assert.True(redemption.Value.WasLateCommit);
        Assert.Equal(CodeReservationStatus.Committed, reservation.Status);
        Assert.Equal(1, definition.CommittedRedemptions);
    }

    [Fact]
    public void Multiple_partial_compensations_are_monotonic_and_cannot_exceed_discount()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();
        var quote = GrowthTestData.CreateQuote(
            definition,
            GrowthTestData.RefereeTenantId,
            grossAmountCents: 10_000
        );
        var reservation = GrowthTestData.CreateReservation(definition, quote);
        var redemption = reservation
            .Commit(
                IdempotencyKey.Create("commit-1").Value,
                PayloadFingerprint.Create(GrowthTestData.Sha('a')).Value,
                Guid.NewGuid(),
                allowLateCommit: false,
                GrowthTestData.NowUtc.AddMinutes(1)
            )
            .Value;
        Assert.True(definition.CommitReservedUse(GrowthTestData.NowUtc.AddMinutes(1)).IsSuccess);

        var first = CodeCompensation
            .Create(
                redemption,
                CodeCompensationType.ProportionalAdjustment,
                Money.Create(200, "USD").Value,
                priorCumulativeAdjustmentAmountCents: 0,
                "partial refund 1",
                Guid.NewGuid(),
                IdempotencyKey.Create("comp-1").Value,
                PayloadFingerprint.Create(GrowthTestData.Sha('b')).Value,
                GrowthTestData.NowUtc.AddDays(1)
            )
            .Value;
        Assert.False(first.IsFinal);
        Assert.True(
            reservation.RecordCompensation(
                first.Id,
                first.IsFinal,
                GrowthTestData.NowUtc.AddDays(1)
            ).IsSuccess
        );
        Assert.Equal(CodeReservationStatus.Committed, reservation.Status);

        var final = CodeCompensation
            .Create(
                redemption,
                CodeCompensationType.ProportionalAdjustment,
                Money.Create(800, "USD").Value,
                first.CumulativeAdjustmentAmountCents,
                "partial refund 2",
                Guid.NewGuid(),
                IdempotencyKey.Create("comp-2").Value,
                PayloadFingerprint.Create(GrowthTestData.Sha('c')).Value,
                GrowthTestData.NowUtc.AddDays(2)
            )
            .Value;
        Assert.True(final.IsFinal);
        Assert.True(
            reservation.RecordCompensation(
                final.Id,
                final.IsFinal,
                GrowthTestData.NowUtc.AddDays(2)
            ).IsSuccess
        );
        Assert.Equal(CodeReservationStatus.Compensated, reservation.Status);

        var exceeds = CodeCompensation.Create(
            redemption,
            CodeCompensationType.ProportionalAdjustment,
            Money.Create(1, "USD").Value,
            final.CumulativeAdjustmentAmountCents,
            "invalid extra refund",
            Guid.NewGuid(),
            IdempotencyKey.Create("comp-3").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('d')).Value,
            GrowthTestData.NowUtc.AddDays(3)
        );
        Assert.True(exceeds.IsFailure);
        Assert.Equal("Codes.CodeCompensation.CumulativeAmountExceeded", exceeds.Error.Code);
    }

    [Fact]
    public void Code_hash_and_display_never_reveal_a_plaintext_token()
    {
        const string plaintext = "GIFT-SECRET-NEVER-PERSIST";
        var definition = GrowthTestData.CreateActivePercentageCode();

        Assert.Equal(64, definition.CodeHash.Value.Length);
        Assert.DoesNotContain(plaintext, definition.CodeHash.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(plaintext, definition.Display.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CodeUsageDimension.Tenant)]
    [InlineData(CodeUsageDimension.Subject)]
    public void Scoped_usage_counter_prevents_oversubscription_and_restores_capacity(
        CodeUsageDimension dimension
    )
    {
        var scope = dimension == CodeUsageDimension.Tenant
            ? CodeUsageScopeKey.ForTenant(GrowthTestData.RefereeTenantId).Value
            : CodeUsageScopeKey.Create("1:tenant-subject").Value;
        var counter = CodeUsageCounter
            .Create(
                GrowthTestData.RefereeTenantId,
                Guid.NewGuid(),
                dimension,
                scope,
                maxRedemptions: 1,
                GrowthTestData.NowUtc
            )
            .Value;

        Assert.True(counter.Reserve(GrowthTestData.NowUtc).IsSuccess);
        Assert.True(counter.Reserve(GrowthTestData.NowUtc).IsFailure);
        Assert.True(counter.CommitReserved(GrowthTestData.NowUtc.AddMinutes(1)).IsSuccess);
        Assert.True(counter.Reserve(GrowthTestData.NowUtc.AddMinutes(2)).IsFailure);
        Assert.True(counter.RestoreCommitted(GrowthTestData.NowUtc.AddMinutes(3)).IsSuccess);
        Assert.True(counter.Reserve(GrowthTestData.NowUtc.AddMinutes(4)).IsSuccess);
    }
}
