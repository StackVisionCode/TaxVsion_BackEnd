using TaxVision.Codes.Application.Reservations.ExpireReservation;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.Usage;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

public sealed class CodesReservationApplicationTests
{
    [Theory]
    [InlineData(CodeUsageDimension.Tenant, "Codes.CodeUsageCounter.TenantLimitReached")]
    [InlineData(CodeUsageDimension.Subject, "Codes.CodeUsageCounter.SubjectLimitReached")]
    public async Task Reserve_honors_tenant_and_subject_counters(CodeUsageDimension dimension, string expectedError)
    {
        var definition = GrowthTestData.CreateActivePercentageCode(
            maxRedemptions: 10,
            maxRedemptionsPerTenant: dimension == CodeUsageDimension.Tenant ? 1 : null,
            maxRedemptionsPerSubject: dimension == CodeUsageDimension.Subject ? 1 : null
        );
        var quote = GrowthTestData.CreateQuote(
            definition,
            GrowthTestData.RefereeTenantId,
            subjectId: "limited-subject"
        );
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var quotes = new InMemoryCodeQuoteRepository(quote);
        var reservations = new InMemoryCodeReservationRepository();
        var counters = new InMemoryCodeUsageCounterRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var scopeKey =
            dimension == CodeUsageDimension.Tenant
                ? CodeUsageScopeKey.ForTenant(GrowthTestData.RefereeTenantId).Value
                : CodeUsageScopeKey.ForSubject(quote.Subject).Value;
        var saturated = (
            await counters.GetOrCreateForUpdateAsync(
                GrowthTestData.RefereeTenantId,
                definition.Id,
                dimension,
                scopeKey,
                1,
                GrowthTestData.NowUtc
            )
        ).Value;
        Assert.True(saturated.Reserve(GrowthTestData.NowUtc).IsSuccess);

        var result = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(
                GrowthTestData.RefereeTenantId,
                quote.Id,
                "PaymentApp",
                Guid.NewGuid(),
                $"reserve-{dimension}",
                300
            ),
            definitions,
            quotes,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(expectedError, result.Error.Code);
        Assert.Empty(reservations.Reservations);
        Assert.Equal(1, saturated.ActiveReservations);
    }

    [Fact]
    public async Task Expire_releases_global_tenant_and_subject_availability_exactly_once()
    {
        var definition = GrowthTestData.CreateActivePercentageCode(
            maxRedemptions: 10,
            maxRedemptionsPerTenant: 3,
            maxRedemptionsPerSubject: 2
        );
        var quote = GrowthTestData.CreateQuote(
            definition,
            GrowthTestData.RefereeTenantId,
            subjectId: "expiring-subject"
        );
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var quotes = new InMemoryCodeQuoteRepository(quote);
        var reservations = new InMemoryCodeReservationRepository();
        var counters = new InMemoryCodeUsageCounterRepository();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));

        var reserve = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(
                GrowthTestData.RefereeTenantId,
                quote.Id,
                "PaymentApp",
                Guid.NewGuid(),
                "reserve-before-expiry",
                60
            ),
            definitions,
            quotes,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );
        Assert.True(reserve.IsSuccess);

        var reservation = Assert.Single(reservations.Reservations);
        var tenantCounter = counters.Get(
            GrowthTestData.RefereeTenantId,
            definition.Id,
            CodeUsageDimension.Tenant,
            CodeUsageScopeKey.ForTenant(GrowthTestData.RefereeTenantId).Value
        );
        var subjectCounter = counters.Get(
            GrowthTestData.RefereeTenantId,
            definition.Id,
            CodeUsageDimension.Subject,
            CodeUsageScopeKey.ForSubject(reservation.Subject).Value
        );
        Assert.Equal(1, definition.ActiveReservations);
        Assert.Equal(1, tenantCounter.ActiveReservations);
        Assert.Equal(1, subjectCounter.ActiveReservations);

        clock.Advance(TimeSpan.FromMinutes(2));
        var expireCommand = new ExpireReservationCommand(
            GrowthTestData.RefereeTenantId,
            reservation.Id,
            "expire-reservation-1"
        );
        var first = await ExpireReservationHandler.Handle(
            expireCommand,
            definitions,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );
        var replay = await ExpireReservationHandler.Handle(
            expireCommand,
            definitions,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );
        var differentKeyReplay = await ExpireReservationHandler.Handle(
            expireCommand with
            {
                IdempotencyKey = "expire-reservation-2",
            },
            definitions,
            reservations,
            counters,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.True(differentKeyReplay.IsSuccess);
        Assert.Equal(CodeReservationStatus.Expired, reservation.Status);
        Assert.True(reservation.IsAvailabilityReleased);
        Assert.Equal(0, definition.ActiveReservations);
        Assert.Equal(0, tenantCounter.ActiveReservations);
        Assert.Equal(0, subjectCounter.ActiveReservations);
    }
}
