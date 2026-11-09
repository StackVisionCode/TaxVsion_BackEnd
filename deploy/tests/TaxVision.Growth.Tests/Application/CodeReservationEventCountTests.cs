using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.ExpireReservation;
using TaxVision.Codes.Application.Reservations.ReserveCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Events;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;
using Wolverine;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// GRW-2: un aviso por comando, y solo el commit sale hacia otros servicios. Corre los handlers reales y después
/// los handlers de dominio sobre lo que quedó en el agregado, igual que Wolverine al confirmar.
/// </summary>
public sealed class CodeReservationEventCountTests
{
    private static readonly Guid TenantId = GrowthTestData.RefereeTenantId;

    private readonly CodeDefinition _definition = GrowthTestData.CreateActivePercentageCode();
    private readonly InMemoryCodeReservationRepository _reservations = new();
    private readonly InMemoryCodeRedemptionRepository _redemptions = new();
    private readonly InMemoryCodeUsageCounterRepository _counters = new();
    private readonly FakeBusinessIdempotencyExecutor _idempotency = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(GrowthTestData.NowUtc));
    private readonly InMemoryCodeDefinitionRepository _definitions;

    public CodeReservationEventCountTests() => _definitions = new InMemoryCodeDefinitionRepository(_definition);

    [Fact]
    public async Task Commit_EmitsExactlyOneIntegrationEvent()
    {
        var reservation = await ReserveAsync();
        reservation.ClearDomainEvents();

        var committed = await CommitAsync(reservation, "commit-1");

        Assert.True(committed.IsSuccess);
        var notice = Assert.Single(GrowthDomainEventReplay.Dispatch(reservation.DomainEvents));
        var integration = Assert.IsType<CodeReservationCommittedIntegrationEvent>(notice);
        Assert.Equal(TenantId, integration.TenantId);
        Assert.Equal(reservation.Id, integration.ReservationId);
        Assert.Equal(reservation.Id, integration.AggregateId);
        Assert.Equal(committed.Value.RedemptionId, integration.RedemptionId);
        Assert.Equal(reservation.Payment.Source, integration.PaymentSource);
        Assert.Equal(reservation.Payment.PaymentId, integration.RelatedPaymentId);
        Assert.Equal(reservation.GrossAmount.AmountCents, integration.GrossAmountCents);
        Assert.Equal(reservation.DiscountAmount.AmountCents, integration.DiscountAmountCents);
        Assert.Equal(reservation.NetAmount.AmountCents, integration.NetAmountCents);
        Assert.Equal("USD", integration.Currency);
    }

    // Un proveedor puede repetir el éxito con otro evento: no sale un segundo aviso.
    [Fact]
    public async Task Commit_Replay_RaisesNothingNew()
    {
        var reservation = await ReserveAsync();
        await CommitAsync(reservation, "commit-1");
        reservation.ClearDomainEvents();

        var replay = await CommitAsync(reservation, "commit-2");

        Assert.True(replay.IsSuccess);
        Assert.Empty(reservation.DomainEvents);
    }

    [Fact]
    public async Task Reserve_RaisesOneEventAndEmitsNothing()
    {
        var reservation = await ReserveAsync();

        Assert.IsType<CodeReservationCreatedDomainEvent>(Assert.Single(reservation.DomainEvents));
        Assert.Empty(GrowthDomainEventReplay.Dispatch(reservation.DomainEvents));
    }

    [Fact]
    public async Task Cancel_RaisesOneEventAndEmitsNothing()
    {
        var reservation = await ReserveAsync();
        reservation.ClearDomainEvents();

        var cancelled = await CancelReservationHandler.Handle(
            new CancelReservationCommand(
                TenantId,
                reservation.Id,
                reservation.Payment.Source,
                reservation.Payment.PaymentId,
                "Payment did not complete.",
                "cancel-1"
            ),
            _definitions,
            _reservations,
            _counters,
            _idempotency,
            _clock,
            CancellationToken.None
        );

        Assert.True(cancelled.IsSuccess);
        Assert.IsType<CodeReservationCancelledDomainEvent>(Assert.Single(reservation.DomainEvents));
        Assert.Empty(GrowthDomainEventReplay.Dispatch(reservation.DomainEvents));
    }

    [Fact]
    public async Task Expire_RaisesOneEventAndEmitsNothing()
    {
        var reservation = await ReserveAsync();
        reservation.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var expired = await ExpireReservationHandler.Handle(
            new ExpireReservationCommand(TenantId, reservation.Id, "expire-1"),
            _definitions,
            _reservations,
            _counters,
            _idempotency,
            _clock,
            CancellationToken.None
        );

        Assert.True(expired.IsSuccess);
        Assert.IsType<CodeReservationExpiredDomainEvent>(Assert.Single(reservation.DomainEvents));
        Assert.Empty(GrowthDomainEventReplay.Dispatch(reservation.DomainEvents));
    }

    [Fact]
    public async Task Compensation_RaisesOneEventAndEmitsNothing()
    {
        var reservation = await ReserveAsync();
        var committed = await CommitAsync(reservation, "commit-1");
        reservation.ClearDomainEvents();

        var compensated = reservation.RecordCompensation(Guid.NewGuid(), isFinal: true, GrowthTestData.NowUtc);

        Assert.True(compensated.IsSuccess);
        var raised = Assert.IsType<CodeReservationCompensatedDomainEvent>(Assert.Single(reservation.DomainEvents));
        Assert.Equal(committed.Value.RedemptionId, raised.RedemptionId);
        Assert.Empty(GrowthDomainEventReplay.Dispatch(reservation.DomainEvents));
    }

    // La cola domain-events es durable: tras un reinicio o un reintento, el evento se vuelve a leer del sobre.
    [Fact]
    public async Task EveryDomainEvent_SurvivesTheEnvelopeSerializer()
    {
        var committed = await ReserveAsync();
        await CommitAsync(committed, "commit-1");
        committed.RecordCompensation(Guid.NewGuid(), isFinal: true, GrowthTestData.NowUtc);

        var cancelled = await ReserveAsync("cancel-subject");
        cancelled.Cancel(
            IdempotencyKey.Create("cancel-1").Value,
            PayloadFingerprint.Create(GrowthTestData.Sha('f')).Value,
            "Payment did not complete.",
            GrowthTestData.NowUtc
        );

        var expired = await ReserveAsync("expire-subject");
        expired.Expire(expired.ExpiresAtUtc);

        IDomainEvent[] events = [.. committed.DomainEvents, .. cancelled.DomainEvents, .. expired.DomainEvents];
        Assert.Equal(
            [
                typeof(CodeReservationCancelledDomainEvent),
                typeof(CodeReservationCommittedDomainEvent),
                typeof(CodeReservationCompensatedDomainEvent),
                typeof(CodeReservationCreatedDomainEvent),
                typeof(CodeReservationExpiredDomainEvent),
            ],
            events.Select(e => e.GetType()).Distinct().OrderBy(t => t.Name)
        );

        var serializer = new WolverineOptions().DefaultSerializer;
        Assert.All(
            events,
            domainEvent =>
            {
                var envelope = new Envelope { Data = serializer.WriteMessage(domainEvent) };
                Assert.Equal(domainEvent, serializer.ReadFromData(domainEvent.GetType(), envelope));
            }
        );
    }

    private async Task<CodeReservation> ReserveAsync(string subject = "count-subject")
    {
        var quote = GrowthTestData.CreateQuote(_definition, TenantId, subjectId: subject);
        var reserved = await ReserveCodeHandler.Handle(
            new ReserveCodeCommand(TenantId, quote.Id, "PaymentApp", Guid.NewGuid(), $"reserve-{subject}", 300),
            _definitions,
            new InMemoryCodeQuoteRepository(quote),
            _reservations,
            _counters,
            _idempotency,
            _clock,
            CancellationToken.None
        );
        Assert.True(reserved.IsSuccess);
        return _reservations.Reservations.Single(r => r.Id == reserved.Value.ReservationId);
    }

    private Task<Result<CommitReservationResponse>> CommitAsync(CodeReservation reservation, string key) =>
        CommitReservationHandler.Handle(
            new CommitReservationCommand(
                TenantId,
                reservation.Id,
                reservation.Payment.Source,
                reservation.Payment.PaymentId,
                reservation.SnapshotHash.Value,
                Guid.NewGuid(),
                key
            ),
            _definitions,
            _reservations,
            _redemptions,
            _counters,
            new NotExpectedVerifier(),
            new InMemoryLateCommitReviewRepository(),
            _idempotency,
            _clock,
            CancellationToken.None
        );

    // Solo el commit tardío verifica el pago.
    private sealed class NotExpectedVerifier : IPaymentOutcomeVerifier
    {
        public Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException("Estas pruebas no pasan por un commit tardío.");
    }
}
