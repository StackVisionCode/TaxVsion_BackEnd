using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.CreatePaymentLink;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.ExpirePaymentLink;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using PaymentLink = TaxVision.PaymentClient.Domain.PaymentLinks.PaymentLink;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>BB-14: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea.</summary>
public sealed class PaymentLinkCommandHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly List<object> _published = [];

    [Fact]
    public async Task Create_WhenSaveFails_PublishesNothing()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => CreateAsync(new FailingUnitOfWork()));

        Assert.Empty(_published);
    }

    [Fact]
    public async Task Create_WhenSaved_PublishesTheLink()
    {
        var result = await CreateAsync(new PassingUnitOfWork());

        var created = Assert.IsType<PaymentLinkCreatedIntegrationEvent>(Assert.Single(_published));
        Assert.Equal(result.Value.Token, created.Token);
    }

    [Fact]
    public async Task Expire_WhenSaveFails_PublishesNothing()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => ExpireAsync(new FailingUnitOfWork()));

        Assert.Empty(_published);
    }

    [Fact]
    public async Task Expire_WhenSaved_PublishesTheLink()
    {
        var result = await ExpireAsync(new PassingUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.IsType<PaymentLinkExpiredIntegrationEvent>(Assert.Single(_published));
    }

    private Task<Result<CreatePaymentLinkResponse>> CreateAsync(IUnitOfWork unitOfWork)
    {
        var db = InMemoryPaymentClient.CreateDb();
        return CreatePaymentLinkHandler.Handle(
            new CreatePaymentLinkCommand(
                TenantId,
                Guid.NewGuid(),
                5000,
                "USD",
                PaymentPurposeKind.InvoicePayment,
                "inv-1",
                TimeSpan.FromHours(1),
                Guid.NewGuid()
            ),
            new PaymentLinkRepository(db),
            new PaymentAuditLogWriter(db),
            unitOfWork,
            InMemoryPaymentClient.RecordingBus(_published),
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            CancellationToken.None
        );
    }

    private async Task<Result> ExpireAsync(IUnitOfWork unitOfWork)
    {
        var db = InMemoryPaymentClient.CreateDb();
        var link = PaymentLink
            .Create(
                TenantId,
                Guid.NewGuid(),
                Money.Create(5000, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromHours(1),
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        db.PaymentLinks.Add(link);
        await db.SaveChangesAsync();

        return await ExpirePaymentLinkHandler.Handle(
            new ExpirePaymentLinkCommand(TenantId, link.Id),
            new PaymentLinkRepository(db),
            new PaymentAuditLogWriter(db),
            unitOfWork,
            InMemoryPaymentClient.RecordingBus(_published),
            new FixedCorrelationContext(),
            CancellationToken.None
        );
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Simulated SQL timeout.");
    }

    private sealed class PassingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
