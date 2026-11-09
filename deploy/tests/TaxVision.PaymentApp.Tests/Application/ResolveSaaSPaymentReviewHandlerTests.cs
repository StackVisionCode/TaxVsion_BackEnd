using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Admin.Commands;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El admin registra la decisión tomada a mano; el endpoint nunca reembolsa ni aplica beneficios.</summary>
public sealed class ResolveSaaSPaymentReviewHandlerTests
{
    [Fact]
    public async Task Resolves_the_review_and_leaves_an_audit_entry()
    {
        var payment = FlaggedCancelledPayment();
        var audit = new RecordingAudit();
        var unitOfWork = new FakeUnitOfWork();
        var admin = Guid.NewGuid();

        var result = await ResolveSaaSPaymentReviewHandler.Handle(
            new ResolveSaaSPaymentReviewCommand(payment.Id, "NoActionNeeded", "Customer re-bought seats.", admin),
            new FakeSaaSPaymentRepository { ByIdAnyTenant = payment },
            audit,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("NoActionNeeded", payment.ReviewResolution);
        Assert.Equal(admin, payment.ReviewResolvedByUserId);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(PaymentAuditAction.SaaSPaymentReviewResolved, entry.Action);
        Assert.Equal(admin, entry.ActorUserId);
        Assert.Equal("Customer re-bought seats.", entry.Reason);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Unknown_payment_is_not_found()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await ResolveSaaSPaymentReviewHandler.Handle(
            new ResolveSaaSPaymentReviewCommand(Guid.NewGuid(), "Refunded", null, Guid.NewGuid()),
            new FakeSaaSPaymentRepository(),
            new RecordingAudit(),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("SaaSPayment.NotFound", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static SaaSPayment FlaggedCancelledPayment()
    {
        var payment = SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"review-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        payment.MarkCancelled("BuyerCancelled", DateTime.UtcNow);
        payment.FlagForReview("LateProviderSuccess", "pi_123", 4900, "USD", DateTime.UtcNow);
        return payment;
    }

    private sealed class RecordingAudit : IPaymentAuditLogWriter
    {
        public List<PaymentAuditEntry> Entries { get; } = [];

        public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
