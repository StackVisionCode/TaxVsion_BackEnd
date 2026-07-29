using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Consumers;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 11 — cierra el ciclo receipt: Documents terminó de generar el PDF y este
/// consumer guarda el FileId + publica OnboardingReceiptReadyIntegrationEvent con el link mediador
/// de descarga (nunca la URL presignada directa).</summary>
public sealed class OnboardingReceiptGenerationCompletedConsumerTests
{
    private static readonly OnboardingOptions Options_ = new() { AuthPublicBaseUrl = "https://auth.example.com" };

    [Fact]
    public async Task Attaches_receipt_and_publishes_ready_event_when_owner_type_is_onboarding()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();
        var fileId = Guid.NewGuid();

        var evt = new DocumentGenerationCompletedIntegrationEvent
        {
            GenerationId = Guid.NewGuid(),
            DocumentType = "OnboardingReceipt",
            OwnerType = "Onboarding",
            OwnerId = onboarding.Id,
            DocumentVersion = 1,
            FileId = fileId,
            FileName = $"receipt-{onboarding.Id:N}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 12345,
        };

        await OnboardingReceiptGenerationCompletedConsumer.Handle(
            evt,
            onboardings,
            Options.Create(Options_),
            unitOfWork,
            bus,
            correlation,
            NullLogger<DocumentGenerationCompletedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(fileId, onboarding.ReceiptFileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var published = Assert.Single(bus.Published);
        var ready = Assert.IsType<OnboardingReceiptReadyIntegrationEvent>(published);
        Assert.Equal(onboarding.Id, ready.OnboardingId);
        Assert.Equal(fileId, ready.ReceiptFileId);
        Assert.Equal($"https://auth.example.com/onboarding/receipts/{fileId}/download", ready.ReceiptDownloadUrl);
    }

    [Fact]
    public async Task Ignores_generations_for_other_owner_types()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new DocumentGenerationCompletedIntegrationEvent
        {
            GenerationId = Guid.NewGuid(),
            DocumentType = "Invoice",
            OwnerType = "Invoice",
            OwnerId = Guid.NewGuid(),
            DocumentVersion = 1,
            FileId = Guid.NewGuid(),
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
        };

        await OnboardingReceiptGenerationCompletedConsumer.Handle(
            evt,
            onboardings,
            Options.Create(Options_),
            unitOfWork,
            bus,
            correlation,
            NullLogger<DocumentGenerationCompletedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Ignores_events_for_unknown_onboardings()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new DocumentGenerationCompletedIntegrationEvent
        {
            GenerationId = Guid.NewGuid(),
            DocumentType = "OnboardingReceipt",
            OwnerType = "Onboarding",
            OwnerId = Guid.NewGuid(),
            DocumentVersion = 1,
            FileId = Guid.NewGuid(),
            FileName = "receipt.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
        };

        await OnboardingReceiptGenerationCompletedConsumer.Handle(
            evt,
            onboardings,
            Options.Create(Options_),
            unitOfWork,
            bus,
            correlation,
            NullLogger<DocumentGenerationCompletedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
